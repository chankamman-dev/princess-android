using Mono.Cecil;
using Mono.Cecil.Cil;

// ptool stage <depot Managed> <unityaot bcl> <android Managed> <out> <extra dir>
//
// Builds the assembly set il2cpp is given (unityaot class library + the Android player's UnityEngine
// modules + the depot for the rest) and weaves the port's hooks:
//   - Steamworks.NET: every P/Invoke becomes a stub, so SteamAPI.Init just fails and the game runs
//     its no-Steam branches (RestartAppIfNecessary false, Initialized false).
//   - AchievementController.Unlock(int) first calls PrincessAndroid.Bridge.Achievement(id).
//   - MyData.ApplySaveDataUserMain ends with Bridge.Unlocked(gstatus.acvUnlockedIds): achievements
//     already held in a save reach Steam too.
//   - SaveController.FileSave / FileDelete end with Bridge.Saved(): Steam Cloud upload.
//   - AddressablesImpl.ResolveInternalId passes its result through Bridge.MapId: Windows paths
//     become device paths.
//   - AssetBundleRequestOptions.Crc reads 0: the retargeted bundles no longer match the build's CRCs.
static class Stage
{
    public static void Run(string depotManaged, string bcl, string androidManaged, string outDir, string extraDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var f in Directory.GetFiles(outDir, "*.dll")) File.Delete(f);
        var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(depotManaged, "*.dll")) pool[Path.GetFileName(f)] = f;
        if (Directory.Exists(Path.Combine(bcl, "Facades")))
            foreach (var f in Directory.GetFiles(Path.Combine(bcl, "Facades"), "*.dll")) pool[Path.GetFileName(f)] = f;
        foreach (var f in Directory.GetFiles(bcl, "*.dll")) pool[Path.GetFileName(f)] = f;
        foreach (var f in Directory.GetFiles(androidManaged, "*.dll")) pool[Path.GetFileName(f)] = f;
        foreach (var f in Directory.GetFiles(extraDir, "*.dll")) pool[Path.GetFileName(f)] = f;

        var roots = Directory.GetFiles(depotManaged, "*.dll").Select(Path.GetFileName)
            .Where(n => !n.StartsWith("System") && !n.StartsWith("Mono.") && n != "mscorlib.dll" && n != "netstandard.dll")
            .Concat(Directory.GetFiles(androidManaged, "UnityEngine*.dll").Select(Path.GetFileName))
            .Concat(Directory.GetFiles(extraDir, "*.dll").Select(Path.GetFileName))
            .Concat(new[] { "mscorlib.dll", "System.dll", "System.Core.dll" })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var q = new Queue<string>(roots);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            if (!seen.Add(n)) continue;
            if (!pool.TryGetValue(n, out var p)) { Console.WriteLine("missing reference " + n); continue; }
            using var m = ModuleDefinition.ReadModule(p);
            foreach (var r in m.AssemblyReferences) q.Enqueue(r.Name + ".dll");
        }
        foreach (var n in seen.Where(pool.ContainsKey)) File.Copy(pool[n], Path.Combine(outDir, n), true);
        Console.WriteLine($"staged {Directory.GetFiles(outDir, "*.dll").Length} assemblies");

        var resolver = new MemResolver(outDir);
        resolver.AddSearchDirectory(outDir);
        var rp = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false, InMemory = true };
        var bridge = AssemblyDefinition.ReadAssembly(Path.Combine(outDir, "PrincessAndroid.dll"), rp).MainModule.GetType("PrincessAndroid.Bridge");
        MethodDefinition B(string name) => bridge.Methods.First(x => x.Name == name);

        // Steamworks.NET
        {
            var path = Path.Combine(outDir, "com.rlabrecque.steamworks.net.dll");
            var asm = AssemblyDefinition.ReadAssembly(path, rp);
            int c = 0;
            foreach (var t in asm.MainModule.GetTypes())
                foreach (var me in t.Methods)
                    if (me.IsPInvokeImpl) { StubMethod(me); c++; }
            foreach (var r in asm.MainModule.ModuleReferences.ToList()) asm.MainModule.ModuleReferences.Remove(r);
            asm.Write(path);
            Console.WriteLine($"steamworks: {c} P/Invokes stubbed");
        }

        // Game hooks
        {
            var path = Path.Combine(outDir, "Assembly-CSharp.dll");
            var asm = AssemblyDefinition.ReadAssembly(path, rp);
            var mod = asm.MainModule;

            var awake = mod.GetType("InitLoader").Methods.First(x => x.Name == "Awake");
            var ail = awake.Body.GetILProcessor();
            ail.InsertBefore(awake.Body.Instructions[0], ail.Create(OpCodes.Call, mod.ImportReference(B("Boot"))));

            var unlock = mod.GetType("AchievementController").Methods.First(x => x.Name == "Unlock" && x.Parameters.Count == 1);
            var uil = unlock.Body.GetILProcessor();
            var first = unlock.Body.Instructions[0];
            uil.InsertBefore(first, uil.Create(OpCodes.Ldarg_1));
            uil.InsertBefore(first, uil.Create(OpCodes.Call, mod.ImportReference(B("Achievement"))));

            var myData = mod.GetType("MyData");
            var gstatus = myData.Fields.First(x => x.Name == "gstatus");
            var ids = gstatus.FieldType.Resolve().Fields.First(x => x.Name == "acvUnlockedIds");
            var apply = myData.Methods.First(x => x.Name == "ApplySaveDataUserMain");
            int n1 = BeforeEachRet(apply, (il, at) =>
            {
                il.InsertBefore(at, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(at, il.Create(OpCodes.Ldfld, mod.ImportReference(gstatus)));
                il.InsertBefore(at, il.Create(OpCodes.Ldfld, mod.ImportReference(ids)));
                il.InsertBefore(at, il.Create(OpCodes.Call, mod.ImportReference(B("Unlocked"))));
            });

            var saved = mod.ImportReference(B("Saved"));
            int n2 = 0;
            foreach (var m in mod.GetType("SaveController").Methods.Where(x => x.HasBody && x.Name is "FileSave" or "FileDelete"))
                n2 += BeforeEachRet(m, (il, at) => il.InsertBefore(at, il.Create(OpCodes.Call, saved)));

            // Screen.SetResolution(int, int, bool) -> Bridge.SetResolution (always full screen).
            var setRes = mod.ImportReference(B("SetResolution"));
            int n4 = 0;
            foreach (var t in mod.GetTypes())
                foreach (var m in t.Methods.Where(x => x.HasBody))
                    foreach (var ins in m.Body.Instructions)
                        if (ins.Operand is MethodReference r && r.Name == "SetResolution" && r.DeclaringType.FullName == "UnityEngine.Screen"
                            && r.Parameters.Count == 3 && r.Parameters[2].ParameterType.MetadataType == MetadataType.Boolean)
                        { ins.Operand = setRes; n4++; }
            Console.WriteLine($"Assembly-CSharp: {n4} Screen.SetResolution calls redirected");

            // The game switches Unity's logger off in its release build. Bridge.SetLogEnabled keeps
            // that unless the launcher's developer setting is on: logger.set_logEnabled(x) becomes
            // pop logger; Bridge.SetLogEnabled(x). Application.targetFrameRate = n (the monitor's
            // refresh rate on PC) goes through Bridge.SetTargetFrameRate for the launcher's cap.
            int n3 = 0, n6 = 0;
            var setLog = mod.ImportReference(B("SetLogEnabled"));
            var setFps = mod.ImportReference(B("SetTargetFrameRate"));
            foreach (var t in mod.GetTypes())
                foreach (var m in t.Methods.Where(x => x.HasBody))
                    foreach (var ins in m.Body.Instructions.ToList())
                    {
                        if (ins.Operand is not MethodReference r) continue;
                        if (r.Name == "set_logEnabled" && r.DeclaringType.Name == "ILogger")
                        {
                            // stack: logger, bool -> keep bool in a local, drop logger.
                            var il = m.Body.GetILProcessor();
                            var tmp = new VariableDefinition(mod.TypeSystem.Boolean);
                            m.Body.Variables.Add(tmp);
                            m.Body.InitLocals = true;
                            il.InsertBefore(ins, il.Create(OpCodes.Stloc, tmp));
                            il.InsertBefore(ins, il.Create(OpCodes.Pop));
                            il.InsertBefore(ins, il.Create(OpCodes.Ldloc, tmp));
                            ins.OpCode = OpCodes.Call;
                            ins.Operand = setLog;
                            n3++;
                        }
                        else if (r.Name == "set_targetFrameRate" && r.DeclaringType.FullName == "UnityEngine.Application" && !t.FullName.StartsWith("TMPro.Examples"))
                        {
                            ins.Operand = setFps;
                            n6++;
                        }
                    }
            Console.WriteLine($"Assembly-CSharp: {n6} targetFrameRate writes go through the launcher's cap");
            // Rewired's InputManager/ExternalTools are source files compiled with the Windows defines,
            // so the player reports Platform.Windows and hands Rewired the Windows native-input
            // initializer, which fails on Android and leaves gamepads unseen. Report Android (7) +
            // IL2CPP (2) and no platform initializer: Android's only input source is Unity's.
            var detect = mod.GetType("Rewired.InputManager").Methods.First(x => x.Name == "DetectPlatform");
            int n5 = 0;
            var dins = detect.Body.Instructions;
            for (int i = 1; i < dins.Count; i++)
                if (dins[i].OpCode == OpCodes.Stfld && dins[i].Operand is FieldReference f && dins[i - 1].OpCode.Code == Code.Ldc_I4_1 && f.Name == "platform")
                { dins[i - 1].OpCode = OpCodes.Ldc_I4; dins[i - 1].Operand = 7; n5++; }
                else if (dins[i].OpCode == OpCodes.Stfld && dins[i].Operand is FieldReference g && g.Name == "scriptingBackend" && dins[i - 1].OpCode.Code == Code.Ldc_I4_0 && i > 20)
                { dins[i - 1].OpCode = OpCodes.Ldc_I4_2; n5++; }
            if (n5 != 2) throw new Exception($"Rewired DetectPlatform: expected 2 patches, got {n5}");
            var gpi = mod.GetType("Rewired.Utils.ExternalTools").Methods.First(x => x.Name == "GetPlatformInitializer");
            gpi.Body.Instructions.Clear(); gpi.Body.ExceptionHandlers.Clear();
            var gil = gpi.Body.GetILProcessor();
            gil.Append(gil.Create(OpCodes.Ldnull)); gil.Append(gil.Create(OpCodes.Ret));
            Console.WriteLine("Assembly-CSharp: Rewired platform -> Android/IL2CPP, no Windows initializer");

            // Touch play (PrincessAndroid.TouchAssist): a few of the game's mouse reads go through it.
            var touch = bridge.Module.GetType("PrincessAndroid.TouchAssist");
            MethodReference T(string name) => mod.ImportReference(touch.Methods.First(x => x.Name == name));
            int Redirect(string type, string method, string callee, MethodReference to)
            {
                int c = 0;
                var m = mod.GetType(type).Methods.First(x => x.Name == method && x.HasBody);
                foreach (var ins in m.Body.Instructions)
                    if (ins.Operand is MethodReference r && r.Name == callee && r.DeclaringType.FullName == "UnityEngine.Input")
                    { ins.OpCode = OpCodes.Call; ins.Operand = to; c++; }
                if (c == 0) throw new Exception($"touch: no Input.{callee} in {type}.{method}");
                return c;
            }
            int t1 = Redirect("RewiredController", "Update", "get_mousePosition", T("MousePosition"));
            int t2 = Redirect("RewiredController", "Update", "GetMouseButtonDown", T("MouseButtonDown"));
            int t3 = Redirect("GalleryViewer", "Update", "get_mouseScrollDelta", T("GalleryScroll"));
            int t4 = Redirect("ButtonFieldSelectGroup", "UpdateOther", "GetMouseButtonDown", T("AreaSubmit"));
            Console.WriteLine($"Assembly-CSharp: touch hooks {t1}+{t2} pointer, {t3} gallery scroll, {t4} battle area");

            asm.Write(path);
            Console.WriteLine($"Assembly-CSharp: achievement hook, {n1} unlocked-list hooks, {n2} save hooks, {n3} logger switches via Bridge.SetLogEnabled");
        }

        // Addressables paths
        {
            var path = Path.Combine(outDir, "Unity.Addressables.dll");
            var asm = AssemblyDefinition.ReadAssembly(path, rp);
            var me = asm.MainModule.GetType("UnityEngine.AddressableAssets.AddressablesImpl").Methods.First(x => x.Name == "ResolveInternalId");
            var map = asm.MainModule.ImportReference(B("MapId"));
            int n = BeforeEachRet(me, (il, at) => il.InsertBefore(at, il.Create(OpCodes.Call, map)));
            asm.Write(path);
            Console.WriteLine($"Addressables: {n} ResolveInternalId returns mapped");
        }

        // 2D Animation: CPU deformation only. GPU deformation needs the SKINNED_SPRITE shader variants
        // (a StructuredBuffer of bone matrices), which have no GLES 3.0 form and were not converted.
        {
            var path = Path.Combine(outDir, "Unity.2D.Animation.Runtime.dll");
            if (File.Exists(path))
            {
                var asm = AssemblyDefinition.ReadAssembly(path, rp);
                var me = asm.MainModule.GetType("UnityEngine.U2D.Animation.SpriteSkinUtility").Methods.First(x => x.Name == "CanUseGpuDeformation");
                me.Body = new MethodBody(me);
                var il = me.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldc_I4_0));
                il.Append(il.Create(OpCodes.Ret));
                asm.Write(path);
                Console.WriteLine("2D Animation: GPU deformation off");
            }
        }

        // Bundle CRCs
        {
            var path = Path.Combine(outDir, "Unity.ResourceManager.dll");
            var asm = AssemblyDefinition.ReadAssembly(path, rp);
            var me = asm.MainModule.GetType("UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions").Methods.First(x => x.Name == "get_Crc");
            me.Body = new MethodBody(me);
            var il = me.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));

            // Async bundle and asset loads go through Unity's preload queue one step per frame. The
            // game loads ~1100 bundles behind its logo, which at a handheld's 60 fps is over a minute
            // (on PC the monitor's 144-240 fps hides it). Reading the request's result right away
            // makes Unity finish it synchronously, so the whole set loads within a few frames.
            // PRINCESS_ASYNC_LOADS=1 leaves the loads asynchronous.
            int nSync = 0;
            if (Environment.GetEnvironmentVariable("PRINCESS_ASYNC_LOADS") == null)
            {
                var rm = asm.MainModule;
                var syncFlag = B("get_SyncLoads");
                var abRes = rm.GetType("UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource").Methods.First(x => x.Name == "LoadLocalBundle");
                var assetOp = rm.GetType("UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider").NestedTypes.First(x => x.Name == "InternalOp").Methods.First(x => x.Name == "BeginAssetLoad");
                foreach (var m in new[] { abRes, assetOp })
                {
                    var mil = m.Body.GetILProcessor();
                    foreach (var ins in m.Body.Instructions.ToList())
                    {
                        if (ins.Operand is not MethodReference r || r.DeclaringType.FullName != "UnityEngine.AssetBundle") continue;
                        string getter = r.Name == "LoadFromFileAsync" ? "get_assetBundle" : r.Name is "LoadAssetAsync" or "LoadAssetWithSubAssetsAsync" ? "get_asset" : null;
                        if (getter == null) continue;
                        var reqType = r.ReturnType.Resolve();
                        var get = rm.ImportReference(reqType.Methods.First(x => x.Name == getter));
                        // req -> if (Bridge.SyncLoads) { req.<result>; } -> req  (launcher setting)
                        var after = mil.Create(OpCodes.Nop);
                        mil.InsertAfter(ins, after);
                        mil.InsertBefore(after, mil.Create(OpCodes.Call, rm.ImportReference(syncFlag)));
                        mil.InsertBefore(after, mil.Create(OpCodes.Brfalse, after));
                        mil.InsertBefore(after, mil.Create(OpCodes.Dup));
                        mil.InsertBefore(after, mil.Create(OpCodes.Callvirt, get));
                        mil.InsertBefore(after, mil.Create(OpCodes.Pop));
                        nSync++;
                    }
                }
            }
            asm.Write(path);
            Console.WriteLine($"ResourceManager: bundle CRC checks off, {nSync} bundle/asset loads completed synchronously");
        }
    }

    // Inserts code before every ret. Branches that targeted the ret are moved to the inserted code.
    static int BeforeEachRet(MethodDefinition m, Action<ILProcessor, Instruction> emit)
    {
        var il = m.Body.GetILProcessor();
        int n = 0;
        foreach (var r in m.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToList())
        {
            var marker = il.Create(OpCodes.Nop);
            il.InsertBefore(r, marker);
            emit(il, r);
            foreach (var ins in m.Body.Instructions)
            {
                if (ins.Operand == r && ins != r) ins.Operand = marker;
                if (ins.Operand is Instruction[] arr) for (int k = 0; k < arr.Length; k++) if (arr[k] == r) arr[k] = marker;
            }
            foreach (var h in m.Body.ExceptionHandlers)
            {
                if (h.HandlerEnd == r) h.HandlerEnd = marker;
                if (h.TryEnd == r) h.TryEnd = marker;
            }
            n++;
        }
        return n;
    }

    static void StubMethod(MethodDefinition m)
    {
        m.PInvokeInfo = null;
        m.IsPInvokeImpl = false;
        m.ImplAttributes = 0;
        m.IsPreserveSig = false;
        m.MethodReturnType.MarshalInfo = null;
        m.MethodReturnType.HasFieldMarshal = false;
        foreach (var p in m.Parameters) { p.MarshalInfo = null; p.HasFieldMarshal = false; }
        var body = new MethodBody(m);
        m.Body = body;
        var il = body.GetILProcessor();
        var rt = m.ReturnType;
        // Out parameters must be assigned for the verifier-free il2cpp too: zero them.
        foreach (var p in m.Parameters.Where(p => p.IsOut && p.ParameterType.IsByReference))
        {
            var et = ((ByReferenceType)p.ParameterType).ElementType;
            il.Append(il.Create(OpCodes.Ldarg, p));
            il.Append(il.Create(OpCodes.Initobj, et));
        }
        if (rt.MetadataType != MetadataType.Void)
        {
            switch (rt.MetadataType)
            {
                case MetadataType.Boolean: case MetadataType.Byte: case MetadataType.SByte: case MetadataType.Int16: case MetadataType.UInt16:
                case MetadataType.Int32: case MetadataType.UInt32: case MetadataType.Char:
                    il.Append(il.Create(OpCodes.Ldc_I4_0)); break;
                case MetadataType.Int64: case MetadataType.UInt64:
                    il.Append(il.Create(OpCodes.Ldc_I8, 0L)); break;
                case MetadataType.Single:
                    il.Append(il.Create(OpCodes.Ldc_R4, 0f)); break;
                case MetadataType.Double:
                    il.Append(il.Create(OpCodes.Ldc_R8, 0d)); break;
                case MetadataType.IntPtr: case MetadataType.UIntPtr: case MetadataType.Pointer:
                    il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Conv_I)); break;
                default:
                    if (rt.IsValueType)
                    {
                        var v = new VariableDefinition(rt);
                        body.Variables.Add(v);
                        body.InitLocals = true;
                        il.Append(il.Create(OpCodes.Ldloc, v));
                    }
                    else il.Append(il.Create(OpCodes.Ldnull));
                    break;
            }
        }
        il.Append(il.Create(OpCodes.Ret));
    }
}

// Resolves references from bytes in memory, so assemblies can be rewritten in place while another
// assembly that references them is open.
class MemResolver : BaseAssemblyResolver
{
    readonly string dir;
    readonly Dictionary<string, AssemblyDefinition> cache = new(StringComparer.OrdinalIgnoreCase);
    public MemResolver(string dir) { this.dir = dir; }

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        if (cache.TryGetValue(name.Name, out var a)) return a;
        var p = Path.Combine(dir, name.Name + ".dll");
        if (!File.Exists(p)) return base.Resolve(name);
        a = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(p)), new ReaderParameters { AssemblyResolver = this });
        cache[name.Name] = a;
        return a;
    }
}

// The cloud-save module. Built as a plain Java library against android.jar; build-apk.sh dexes its
// classes together with every runtime dependency, so no Android Gradle plugin is involved.
plugins {
    `java-library`
}

// Android SDK from -PandroidSdk=<dir> or ANDROID_SDK_ROOT (see tools/local.env.example).
val androidSdk = providers.gradleProperty("androidSdk").orNull ?: System.getenv("ANDROID_SDK_ROOT")
    ?: error("set ANDROID_SDK_ROOT or pass -PandroidSdk=<sdk dir>")
val androidJar = "$androidSdk/platforms/android-35/android.jar"

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

repositories {
    mavenCentral()
    google()
}

dependencies {
    compileOnly(files(androidJar))
    implementation("in.dragonbra:javasteam:1.8.0")
    implementation("org.bouncycastle:bcprov-jdk18on:1.78.1")
    implementation("com.google.zxing:core:3.5.3")
    // JavaSteam declares these as runtime-only, but the cloud protocol code uses them directly.
    implementation("com.google.protobuf:protobuf-java:4.31.1")
    implementation("com.squareup.okhttp3:okhttp-jvm:5.3.2")
    // SteamDepot calls the coroutine helpers (Deferred -> CompletableFuture) directly.
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core-jvm:1.10.2")
}

// Every jar the module needs at run time, for d8.
tasks.register<Copy>("collectRuntimeDeps") {
    from(configurations.runtimeClasspath)
    into(layout.buildDirectory.dir("deps"))
}

tasks.withType<JavaCompile> {
    options.encoding = "UTF-8"
}

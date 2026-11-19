plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Release signing: values come from ~/.gradle/gradle.properties or environment variables
// (DESKDECK_STORE_FILE, DESKDECK_STORE_PASSWORD, DESKDECK_KEY_ALIAS, DESKDECK_KEY_PASSWORD).
// Without them, release builds fall back to the debug key so the project still builds anywhere.
fun signingValue(name: String): String? =
    (project.findProperty(name) as String?) ?: System.getenv(name)

android {
    namespace = "com.anirudhkaplish.deskdeck.deskpanel"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.anirudhkaplish.deskdeck.deskpanel"
        minSdk = 24
        targetSdk = 35
        versionCode = 36
        versionName = "0.9.15"
    }

    signingConfigs {
        val storeFile = signingValue("DESKDECK_STORE_FILE")
        if (storeFile != null && file(storeFile).exists()) {
            create("release") {
                this.storeFile = file(storeFile)
                storePassword = signingValue("DESKDECK_STORE_PASSWORD")
                keyAlias = signingValue("DESKDECK_KEY_ALIAS")
                keyPassword = signingValue("DESKDECK_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    buildFeatures {
        buildConfig = true
    }

    lint {
        // Unsupported framework calls can crash old phones before the panel opens.
        fatal += "NewApi"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation("androidx.webkit:webkit:1.12.1")
}

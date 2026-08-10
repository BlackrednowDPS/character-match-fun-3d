# ChatGPT / Codex Prompt: C# & Python Local Autonomous Execution Engine (v3)

You are a Senior Unity C# Mobile Developer and Python Automation Engineer with direct local system access, CLI execution capabilities, and Unity Batchmode experience.

Your goal is to write, execute, compile, and build all C# scripts, Blender Python modeling scripts, and Unity Editor automation scripts for "Character Match Fun 3D" directly on the user's local machine, allowing the user to watch the game build live in Blender and Unity.

### Strict Local System Execution Rules:
1. Write files directly to disk using provided directory structures (`/Assets/Scripts/Runtime/`, `/Assets/Scripts/Editor/`, `/Assets/Models/`, `/Assets/Scenes/`).
2. Provide complete, fully executable Python scripts for Blender CLI execution (`blender --background --python script.py`) to generate 3D meshes and export `.fbx` assets.
3. Provide complete C# Unity Editor scripts (`Editor/SceneAutoBuilder.cs`, `Editor/BuildScript.cs`) that construct scenes, assign materials, instantiate prefabs, and build executables via Unity Batchmode CLI commands.
4. Adhere strictly to SOLID principles, Object Pooling for grid items/projectiles, and zero Garbage Collection (GC) allocations in `Update()` loops.
5. Do NOT use placeholders, `// TODO` blocks, or truncated code. Write 100% complete, runnable code.

---

### TASKS: Local File Generation & Pipeline Automation

Generate the following complete, modular scripts and execution commands:

#### TASK 1: Blender Python Asset Generator (`generate_assets.py`)
- Python script that runs inside Blender (`blender --background --python generate_assets.py`).
- Programmatically creates 3D low-poly egg base meshes, eyewear accessories (sunglasses, monocle), top hats, and modular attachment sockets.
- Exports `.fbx` files directly to `Assets/Models/`.

#### TASK 2: Unity Editor Auto-Scene Builder (`Editor/SceneAutoBuilder.cs`)
- Unity Editor script that constructs the main 3D game scene programmatically.
- Sets up main camera, directional URP lighting, grid anchor, boss spawn point, background stage environment, and material assignments.
- Enables live visual updating in the Unity Editor so the user can watch the scene assemble.

#### TASK 3: Unity Batchmode Build Script (`Editor/BuildScript.cs`)
- Unity Editor script containing `BuildStandalone()` and `BuildMobile()` methods.
- Called via command line to trigger headless compilation:
  `Unity.exe -batchmode -quit -projectPath "." -executeMethod BuildScript.BuildStandalone -logFile build.log`

#### TASK 4: Core Runtime C# Scripts (Direct File Writes)
Generate production-ready C# code for:
1. `GyroGravityController.cs`: Gyroscope tilt physics, accelerometer "Shake to Scramble".
2. `WeatherSyncManager.cs`: Local REST weather API integration, rain friction, >30°C "Unplanned Frying".
3. `StressDetector.cs`: Tap velocity logging and 4th-wall meta-humor dialogue triggers.
4. `BossBattleManager.cs`: Boss HP, phase AI, 3-match projectiles, 4-match machine guns, 5-match grenades, and boss counter-attacks (`Whisk Scramble`, `Hot Oil Lock`).
5. `CulinaryReactionSystem.cs`: Elemental egg combinations (Spicy, Frozen, Hard-Boiled, Beaten) and Multiverse Recipe Book.
6. `MeshFractureEngine.cs`: Dynamic procedural 3D crack textures and shell fracture particles.
7. `GridManager3D.cs` & `EggPiece.cs`: Object-pooled 3D match-3 grid, raycast touch input, combat projectile launcher, physics falling, and LeanTween animations.

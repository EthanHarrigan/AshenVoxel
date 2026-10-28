# Unity baseline and verification boundary

Pinned editor: **Unity 6.3 LTS 6000.3.27f1**. Pinned graphics package: **URP 17.3.0**. Pinned input package: **Input System 1.17.0**. These are deliberate project pins; the environment has not resolved the manifest in Unity and contains no generated package lock.

## Evidence inspected

- [Official editor release notes](https://unity.com/releases/editor/whats-new/6000.3.27f1) and [Unity support policy](https://unity.com/releases/unity-6/support): the selected LTS baseline. LTS provides a support policy, not universal addon compatibility.
- [URP 17.3 API documentation](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html): page metadata identifies package 17.3.0 and Unity 6000.3 documentation links.
- [Input System 1.17 manual](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.17/manual/index.html): package version 1.17.0.
- [Unity Graphics 6000.3 source: pipeline asset](https://raw.githubusercontent.com/Unity-Technologies/Graphics/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs): editor-only public `Create(ScriptableRendererData)` exists. Several light/shadow property setters are internal, so this project's Editor setup uses checked serialized asset fields for those settings.
- [Renderer data source](https://raw.githubusercontent.com/Unity-Technologies/Graphics/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRendererData.cs) and [postprocess source](https://raw.githubusercontent.com/Unity-Technologies/Graphics/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.cs): confirm the public postprocess-data field, resource reload on renderer enable, and the package's default postprocess asset path.

The source links track a branch rather than an immutable commit. Record the resolved package lock and actual installed source revision after first import. Source inspection reduced API mistakes; it is not equivalent to compiling this project with the installed editor.

## Executed outside Unity

The core compiles with C# 9 against .NET Standard 2.1 reference APIs and passes its standalone checks. DDA also passes a separate exhaustive cell-box oracle. The actual mesher has been compiled and exercised against a small Unity vector/mesh shim, including face, winding and vertex-occlusion oracles. That establishes algorithm behavior under the shim; it does not validate Unity Mesh upload, shader compilation, SRP resources, editor callbacks or rendering.

## First real Editor run must establish

1. Manifest resolution succeeds and package versions match the pins. Commit the generated `Packages/packages-lock.json` after reviewing it.
2. All project assemblies compile without errors. Keep the core's engine-free boundary and the project's .NET Standard 2.1 API baseline.
3. Editor setup creates the renderer/pipeline assets, assigns postprocess resources, activates the pipeline and retains runtime-created shaders in the player build.
4. The forest scene imports with its bootstrap script reference intact. Terrain is visible, correctly wound and shadowed, with intended material colors and atmosphere.
5. New Input System input, grid collision, editing, gravity, boss attack and save/load work in play mode and a development player.
6. Unity-side mesh regression/oracle checks pass. Profile the player on named CPU/GPU hardware before adopting any FPS target.

The full procedure and batch commands are in [PLAYTEST.md](PLAYTEST.md). Addons should be admitted individually against editor, pipeline, input, target platform and ownership requirements; no blanket compatibility claim is made.

The standalone mesh reproduction command is `python3 Tools/run_mesh_checks.py`. It uses the checked-in minimal vector/mesh shim and invokes the same regression/oracle source files as the Unity menu.

# Volumetric Smoke for Unity

A Unity plugin for rendering volumetric smoke that dynamically conforms to a skinned mesh. The effect is achieved by voxelizing the mesh on the GPU each frame, applying procedural noise and Gaussian blur to the resulting volume texture, and ray-marching it in a custom shader.

Developed as the final project for a Bachelor's degree at Babes-Bolyai University, Cluj-Napoca, Romania.

## How to install

In the Unity Editor menu bar, click on `Assets -> Import Package -> Custom Package`, then select `VolumetricSmoke.unitypackage`.
On the pop-up, click `Import`. The package files should appear in the `Assets` window of the editor.

Next, create a new Material and assign the shader `Shaders/VolumetricSmoke.shader`.
Then, on the object you want to render as smoke, add `Scripts/SmokeManager.cs` as a component. Keep in mind that the object must have a SkinnedMesh component attached.
In the **References** section of the script, assign the following files:

| Field | Value |
|---|---|
| **Voxelizer Shader** | `Packages/Voxelizer/Shaders/Voxelizer.compute` |
| **Gaussian Blur Shader** | `Shaders/GaussianBlurCompute.compute` |
| **Noise Shader** | `Shaders/NoiseCompute.compute` |
| **Smoke Material** | the material created in the previous step |
| **Skinned Mesh to Voxelize** | an object with a SkinnedMesh component |

Lastly, add a Cube object to the scene and change its material to the one created previously. This is the place where the smoke is rendered.

## How to use

Both the `SmokeManager` component and the `VolumetricSmoke` shader can be modified to the user's liking via the exposed parameters.

### SmokeManager component

| Parameter | Default | Description |
|---|---|---|
| **Resolution** | `64` | Sets the resolution of the voxel grid (meaning the grid's resolution is 64 x 64 x 64). It is recommended to use values below 64 for optimal performance |
| **Detail Noise Scale** | `10` | The scale of the Voronoi noise used for detail. This can be tuned to the user's liking without major impact on performance |
| **Erosion Noise Scale** | `10` | The scale of the Voronoi noise used for erosion. This can be tuned to the user's liking without major impact on performance |
| **Enable Smoothing** | `True` | Whether the voxels should be smoothed. Although disabling it can boost performance, the effect will look worse visually |
| **Blur Strength** | `1` | How *smoothed* should the voxels be. This can be tuned to the user's liking without major impact on performance |
| **Update Voxels** | `True` | Whether the effect follows the animation of the object |
| **Fill Voxels** | `True` | Whether the object should be "filled with smoke" or only its outline |

### VolumetricSmoke shader (the material)

| Parameter | Default | Description |
|---|---|---|
| **Smoke Color** | `White` | The color of the smoke (duh) |
| **Wind Direction** | `(0, -3, 1, 0)` | Makes the smoke *move* in a given direction. This can be tuned to the user's liking |
| **Step Size** | `0.01` | Size of the raymarching step. It is recommended to not modify this value unless the performance is too slow |
| **Shadow Step Size** | `0.01` | Size of the second raymarching step. It is recommended to not modify this value unless the performance is too slow |
| **Density Multiplier** | `1` | Controls how thick the smoke is |
| **Shadow Strength** | `1` | Controls how dark the shadows are |
| **Absorption** | `0.5` | Controls the value of the absorption coefficient |
| **Scattering** | `0.5` | Controls the value of the scattering coefficient |
| **Erosion** | `0.5` | Controls the erosion effect of the smoke |
| **Forward Phase Function g** | `0.4` | Controls the anisotropy factor of the forward phase function. This replicates the nice effect of seeing the sunset through clouds |
| **Backward Phase Function g** | `-0.4` | Same as above, but the effect is visible when looking *from* the light source |
| **Phase Weight** | `0.5` | Controls which phase function is more visible |
| **Shadow Threshold** | `0.1` | Optimization parameter for the second raymarch. This value shouldn't be modified unless it looks better in the user's eyes |
| **Volume Texture** | `-` | 3D texture that contains the smoothed voxels. This parameter shouldn't be touched as it will be automatically generated each frame by the `SmokeManager` script |
| **Noise Texture** | `-` | 3D texture that contains the generated Voronoi Noise. This parameter shouldn't be touched as it will be automatically generated each frame by the `SmokeManager` script |

## License

This project's own code is licensed under the MIT License (see [LICENSE](LICENSE)).

The `VolumetricSmoke/Packages/Voxelizer` folder contains third-party code from [mattatz/unity-voxel](https://github.com/mattatz/unity-voxel), licensed under its own MIT License (see [LICENSE](VolumetricSmoke/Packages/Voxelizer/LICENSE)). Huge thanks!

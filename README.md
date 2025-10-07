# FinalProject

A Unity project containing the required scene(s) and scripts for the Masters project, built on top of Meta's Passthrough Camera API samples and on-device ML/CV with Unity Sentis.

- Repository: [`Julian-Marchington/FinalProject`](https://github.com/Julian-Marchington/FinalProject)
- Unity versions tested: 6.2
- Target device: Meta Quest 3 / 3S (Horizon OS v74+)

## Quick Start

1. Install Git LFS (large files are tracked):
   ```bash
   git lfs install
   ```
2. Clone the repo:
   ```bash
   git clone https://github.com/Julian-Marchington/FinalProject.git
   ```
3. Open the project in Unity 6
4. In Unity, open `Meta > Tools > Project Setup Tool` and apply all suggested fixes.
5. Switch build target to Android: `File > Build Profiles` (or `File > Build Settings`) → Android → Switch Platform.
6. Open the `Assets/Scenes/VRGAZE.unity` scene and make sure to build and run to the device, all information will be saved on-device.

## Key Scripts

- Passthrough access and utilities: `Assets/PassthroughCameraApiSamples/PassthroughCamera/Scripts/`
  - `WebCamTextureManager.cs` (camera bring-up/permissions)
  - `PassthroughCameraUtils.cs`
- Gaze/face pipeline and project logic: `Assets/Scripts/` and `Assets/New/`
  - Examples: `VRGazeDetection.cs`, `NewGazeDetection.cs`, `SentisGazeRunner.cs`, `YoloV8FaceDetector.cs`, `FaceDetectionManager.cs`, `QuestMicrophoneManager.cs`

## Requirements

- Unity 6
- Packages:
  - Meta MR Utility Kit + All in one SDK
  - Unity Sentis (`com.unity.sentis`)
- Device: Quest 3 / 3S running Horizon OS v74+
- Permissions at runtime: `android.permission.CAMERA` and `horizonos.permission.HEADSET_CAMERA`

Use `Meta > Tools > Project Setup Tool` to install/enable required packages and XR features. Ensure Passthrough is enabled in device settings.

## Build and Deploy (Quest)

1. Open `File > Build Profiles` (or `Build Settings`) and choose Android.
2. Add your active scene(s) to the build list (e.g., `VRGAZE.unity` or a sample scene).
3. Recommended Player Settings:
   - Identification → Package Name: set a unique package (e.g., `com.yourorg.finalproject`).
   - XR Plug-in Management → OpenXR (install Meta OpenXR provider if prompted). Select Meta features as needed.
4. Connect the Quest via USB, enable Developer Mode and USB debugging.
5. Click Build and Run.

## Using the Passthrough Camera

The samples show the canonical integration. Minimal flow:

- Place `WebCamTextureManagerPrefab` into your scene.
- At runtime, wait for permissions and `WebCamTexture` initialization, then consume `WebCamTextureManager.WebCamTexture`.

### Set up from scratch

- Add core building blocks
  1) Create a new empty scene.
  2) Add `Meta > Tools > Building Blocks > Camera Rig` (adds rig, main camera, OVR).
  3) Add `Meta > Tools > Building Blocks > Passthrough` (adds `OVRPassthroughLayer`). Ensure passthrough is enabled on device.

- Add passthrough camera feed
  4) Drag `Assets/PassthroughCameraApiSamples/PassthroughCamera/Prefabs/WebCamTextureManagerPrefab.prefab` into the scene.
  5) Optionally place `WebCamTextureManagerPrefab` near the origin; no extra config needed.

- Bind the Quest camera feed to your pipeline
  6) Drag the `Assets/OldVRGaze/_Other/PCAFeedbinder.prefab` (shown as “FeedBinder” in the scene) into the scene.
  7) In the FeedBinder, set:
     - `PCAFeedBinder.webCamTextureManager` → reference the `WebCamTextureManagerPrefab`’s `PassthroughCameraSamples.WebCamTextureManager` component.

- Add your gaze/voice/cues GameObject (mirrors `VRGazeDetector`)
  8) Create an empty GameObject named `VRGazeDetector`.
  9) Add components in this order, wiring references like the `VRGaze.unity` scene:
     - `AudioSource` (default is fine).
     - `RetinaFaceRunner`:
       - `pcaFeed` → reference the `PCAFeedBinder` on “FeedBinder”.
       - Keep `useQuestPCA` checked; leave model/params as in the scene.
       - `audioSource` → the `AudioSource` on `VRGazeDetector`.
       - Assign audio clips if you’re using the cues (`Addressed.mp3`, `LookingAt.mp3`) from `Assets/`.
     - `CuePresenter`:
       - `hud` → the HUD script on the rig (see below).
       - `audioSrc` → the `AudioSource` on `VRGazeDetector`.
     - `GroupAttentionFromRetina`:
       - `retina` → `RetinaFaceRunner` (above).
     - `GazeRayVisualizer`:
       - `retina` → `RetinaFaceRunner`.
       - `cam` → the `Camera` on `CenterEyeAnchor` under the rig.
     - `VisualCueOverlay`:
       - Leave sprites as your defaults (listen/speak) from `Assets/`.
     - `VisualCueXRProbe`:
       - `overlay` → `VisualCueOverlay` (above).
       - `cues` → `CuePresenter`.
       - `hud` → the HUD on the rig (see next bullet).
     - `TurnTakingOrchestrator_simple`:
       - `attention` → `GroupAttentionFromRetina`.
       - `vad` → `MicVAD_simple`.
       - `cues` → `CuePresenter`.
       - Keep your configured timings/thresholds.
     - `MicVAD_simple`:
       - Leave sample rate, thresholds as in the scene or adjust as needed.

- Hook up HUD/log panels on the rig
  10) On `[BuildingBlock] Camera Rig`, you’ll see:
      - `ControllerUIStatusUGUI`:
        - `retina` → `RetinaFaceRunner` on `VRGazeDetector`.
        - `head` → `CenterEyeAnchor` transform.
        - `hand` → `RightHandAnchor` transform.
      - `UiLogOverlayUGUI`:
        - `head` → `CenterEyeAnchor`.
        - `hand` → `RightHandAnchor`.

- Final checks
  11) Ensure build target is Android and OpenXR/Meta features are enabled via `Meta > Tools > Project Setup Tool`.
  12) Add your active scene to Build Settings and Build & Run to a Quest 3/3S (Horizon OS v74+). Passthrough must be enabled on device.

Notes:
- Run on device (Quest) with Passthrough enabled; Link/SIM is not supported for Passthrough Camera API.
- Only one `WebCamTextureManager` may be active at a time.
- Set `RequestedResolution` on `WebCamTextureManager` if you want a specific resolution, otherwise the highest supported is used.

## Troubleshooting

- Permissions denied: uninstall the app and rebuild, or grant via ADB:
  ```bash
  adb shell pm grant <your.package.name> com.horizonos.permission.HEADSET_CAMERA
  ```
- Black texture / no feed:
  - Verify device Passthrough is enabled and permissions granted.
  - Ensure only one permission requester is used (avoid conflicts with other scripts).
- OpenXR + Unity 2022: Environment Depth is not supported; markers in MultiObjectDetection may not align. Use Unity 6 if needed.
- One camera at a time: `WebCamTexture` limitation. Disable/re-enable manager to switch eyes.

## License

This repository includes upstream Meta samples and assets. See `LICENSE` and `LICENSE.txt` for details. Model files under `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model` are under MIT per upstream.

## Acknowledgements

Based on Meta Passthrough Camera API samples and Unity Sentis examples. See project root `README.md` for the full upstream documentation.


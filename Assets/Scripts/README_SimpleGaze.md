# Simple Gaze Detection Setup Guide

This guide will help you set up a simple gaze detection system using the MobileNetV2 ONNX model from the [yakhyo/gaze-estimation](https://github.com/yakhyo/gaze-estimation) repository.

## What You Need

1. **MobileNetV2 ONNX Model**: Download from the [yakhyo/gaze-estimation repository](https://github.com/yakhyo/gaze-estimation)
2. **Unity with Sentis**: Make sure you have Unity Sentis package installed
3. **Webcam**: Any standard webcam will work

## Step-by-Step Setup

### 1. Download the ONNX Model

1. Go to [https://github.com/yakhyo/gaze-estimation](https://github.com/yakhyo/gaze-estimation)
2. Download the MobileNetV2 model weights
3. Convert to ONNX format using the provided scripts in that repository
4. Place the `.onnx` file in your Unity project's `Assets` folder

### 2. Set Up the Scene

#### Option A: Automatic Setup (Recommended)
1. Create an empty GameObject in your scene
2. Add the `SimpleGazeSetup` script to it
3. The script will automatically create all necessary UI elements
4. Press Play to see the setup in action

#### Option B: Manual Setup
1. Create a Canvas in your scene
2. Add TextMeshPro text elements for status and gaze display
3. Create an empty GameObject and add the `SimpleGazeDetection` script
4. Assign the ONNX model to the `gazeModel` field
5. Connect the UI text references

### 3. Configure the Model

1. Select the GameObject with `SimpleGazeDetection`
2. In the Inspector, assign your ONNX model to the `Gaze Model` field
3. Adjust webcam settings if needed:
   - `Webcam Index`: Usually 0 for the default camera
   - `Target Width/Height`: Keep at 224x224 for best performance

### 4. Test the System

1. Press Play in Unity
2. The system will automatically initialize the webcam and load the model
3. Press the **A button** (or spacebar) to start/stop gaze detection
4. Look at the camera and move your eyes around
5. Watch the UI display your gaze direction (Yaw and Pitch)

## Understanding the Output

- **Yaw**: Left/Right gaze direction (-180° to +180°)
  - Negative values = Looking left
  - Positive values = Looking right
  - 0° = Looking straight ahead

- **Pitch**: Up/Down gaze direction (-90° to +90°)
  - Negative values = Looking down
  - Positive values = Looking up
  - 0° = Looking straight ahead

## Troubleshooting

### Common Issues

1. **"No webcam devices found"**
   - Make sure your webcam is connected and not being used by another application
   - Try changing the `Webcam Index` value

2. **"Model load failed"**
   - Ensure the ONNX file is properly imported into Unity
   - Check that Unity Sentis is installed and working
   - Verify the model file isn't corrupted

3. **Poor gaze detection accuracy**
   - Ensure good lighting conditions
   - Position yourself directly in front of the camera
   - Keep your face clearly visible
   - The model works best with faces that are clearly visible and well-lit

### Performance Tips

- Keep the target resolution at 224x224 for best performance
- The system processes frames continuously when enabled, so disable it when not needed
- Use GPU compute backend for better performance (requires compatible GPU)

## Customization

### Adding More Features

The `SimpleGazeDetection` script provides several public methods you can use:

```csharp
// Get current gaze direction
Vector2 gaze = gazeDetection.GetGazeDirection();

// Check if user is looking at camera
bool isLooking = gazeDetection.IsLookingAtCamera();

// Change webcam
gazeDetection.SetWebcamIndex(1);
```

### Modifying the UI

You can customize the UI by:
1. Modifying the `SimpleGazeSetup` script
2. Creating your own UI elements and connecting them manually
3. Adding visual indicators for gaze direction (arrows, crosshairs, etc.)

## Technical Details

- **Input Format**: 224x224 RGB images with ImageNet normalization
- **Output Format**: 2 values representing yaw and pitch angles
- **Processing**: Real-time webcam frame processing using Unity Sentis
- **Backend**: GPU Compute (CUDA/Metal) for optimal performance

## Support

If you encounter issues:
1. Check the Unity Console for error messages
2. Verify all dependencies are properly installed
3. Ensure your ONNX model is compatible with Unity Sentis
4. Check that your webcam is working in other applications

## Next Steps

Once you have basic gaze detection working, you can:
1. Add gaze tracking over time
2. Implement gaze-based interactions
3. Add calibration features
4. Integrate with VR/AR applications
5. Add face detection preprocessing for better accuracy


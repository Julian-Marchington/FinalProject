# Man Look Around Scripts

This set of scripts allows Man prefabs in your Unity scene to look around randomly and then look at the MainCamera for a specified duration, with comprehensive logging.

## Scripts Overview

### 1. ManLookAroundScript.cs
The main script that controls individual Man prefab behavior.

**Features:**
- Random head rotation within configurable angles
- Smooth rotation to look at MainCamera
- Automatic return to original position
- Comprehensive logging for debugging
- Configurable timing and rotation parameters

**Inspector Settings:**
- `lookAroundDuration`: How long to look around randomly (default: 3 seconds)
- `lookAtCameraDuration`: How long to look at camera (default: 2 seconds)
- `rotationSpeed`: Speed of rotation when looking at camera (default: 2)
- `maxRotationAngle`: Maximum rotation angle for random looking (default: 45 degrees)
- `headTransform`: Reference to the head transform (auto-detected if null)
- `mainCamera`: Reference to MainCamera (auto-detected if null)

### 2. ManLookAroundManager.cs
Manages multiple Man prefabs and coordinates their behavior.

**Features:**
- Automatic discovery of Man prefabs in scene
- Coordinated timing with random delays
- Centralized control for all Man prefabs
- Runtime management capabilities

**Inspector Settings:**
- `enableRandomTiming`: Enable random delays between Man prefabs (default: true)
- `minDelayBetweenLooks`: Minimum delay between Man prefabs (default: 1 second)
- `maxDelayBetweenLooks`: Maximum delay between Man prefabs (default: 3 seconds)
- `manScripts`: Manual list of Man prefab scripts

### 3. ManLookAroundUI.cs
Provides UI controls for the Man prefab behavior.

**Features:**
- Button controls for all major functions
- Automatic manager discovery
- Public methods for UnityEvents integration

## Setup Instructions

### Option 1: Automatic Setup (Recommended)
1. Add the `ManLookAroundManager` script to any GameObject in your scene
2. The manager will automatically find all Man prefabs and add the `ManLookAroundScript` to them
3. The scripts will automatically find the head transform and MainCamera

### Option 2: Manual Setup
1. Add the `ManLookAroundScript` directly to each Man prefab
2. Manually assign the head transform reference in the inspector
3. The script will automatically find the MainCamera

### Option 3: UI Integration
1. Add the `ManLookAroundUI` script to a UI GameObject
2. Assign UI buttons to the script's button references
3. The UI will automatically connect to the manager

## Usage

### Basic Behavior
- Man prefabs will automatically start looking around randomly
- After looking around, they will look at the MainCamera for the specified duration
- They will then return to their original position
- This cycle repeats indefinitely

### Control Methods
- **Pause/Resume**: Use the manager's `PauseAllManPrefabs()` and `ResumeAllManPrefabs()` methods
- **Force Look at Camera**: Use `MakeAllLookAtCamera()` to make all Man prefabs look at camera simultaneously
- **Reset Positions**: Use `ResetAllToOriginalPositions()` to return all Man prefabs to their original rotations

### Logging
The scripts provide comprehensive logging:
- When Man prefabs start looking around
- When they look at the camera
- When they finish looking at the camera
- When they return to original positions
- Any errors or warnings

## Customization

### Timing Adjustments
- Modify `lookAroundDuration` and `lookAtCameraDuration` in the individual scripts
- Adjust `minDelayBetweenLooks` and `maxDelayBetweenLooks` in the manager for coordinated timing

### Rotation Behavior
- Change `maxRotationAngle` to control how far Man prefabs look around
- Adjust `rotationSpeed` to control how fast they turn to look at the camera

### Head Transform
- Manually assign the head transform if the auto-detection doesn't work
- The script searches for transforms with "head" in the name

## Troubleshooting

### Common Issues
1. **Man prefabs not moving**: Check if the head transform is correctly assigned
2. **Not looking at camera**: Ensure MainCamera is tagged as "MainCamera"
3. **Scripts not found**: Make sure the manager script is in the scene

### Debug Information
- Check the Console for detailed logging
- Verify all references are properly assigned in the Inspector
- Ensure the Man prefabs have the correct hierarchy structure

## Example Scene Setup

1. Create an empty GameObject named "ManLookAroundManager"
2. Add the `ManLookAroundManager` script to it
3. The manager will automatically find and configure all Man prefabs
4. Run the scene to see the behavior in action

## Performance Notes

- The scripts use coroutines for smooth animation
- Rotation calculations are optimized for performance
- Multiple Man prefabs can run simultaneously without performance issues
- Consider disabling the scripts if not needed during gameplay


# Pawlygon VRC Facetracking
Face tracking for avatars with Unified Expressions blendshapes, using VRChat's native face tracking.

[![Build Release](https://github.com/PawlygonStudio/VRC-Facetracking/actions/workflows/release.yml/badge.svg)](https://github.com/PawlygonStudio/VRC-Facetracking/actions/workflows/release.yml)

## Requisites
* Unity 2022.3 with a VRChat Avatars SDK that supports native face tracking (3.11.0 beta or newer)
* Avatar with [Unified Expression Blendshapes](https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes) (ARKit names are matched too)  
  → See the full list of [Supported UE Blendshapes](./UEBlendshapes.md)

VRCFury and Modular Avatar aren't required, but work alongside it.

## How to use it
1. Import Pawlygon VRC Facetracking using [VRChat Creator Companion](https://vcc.pawlygon.net/) or import the [unity package manually](https://github.com/PawlygonStudio/VRC-Facetracking/releases/latest).
2. Drag `Packages/Pawlygon - VRC Facetracking/Prefabs/!Pawlygon - Face Tracking` anywhere inside your avatar.
3. Upload. That's it.

Everything is applied to the copy of the avatar the SDK uploads, the same way VRCFury works: your avatar, its descriptor and its animators in the scene aren't changed. To see the result without uploading, right-click the avatar in the Hierarchy and choose **!Pawlygon > Face Tracking > Build Test Copy**.

The prefab contains these components. Each one shows what it will do in its inspector:

| Component | What it does |
|---|---|
| **Face Tracking** | Sets up native face tracking on the avatar descriptor: meshes, blendshape mapping, visemes, eyelids and eye gaze. Replaces any face tracking setup already on the descriptor. |
| **Face Tracking Parameters** | Keeps animators made for OSC face tracking working (e.g. Face Tracking Extras). Parameters like `FT/v2/JawX` or `OSCm/Proxy/FT/v2/EyeLidLeft` used by the avatar's animators are fed from native face tracking. |
| **Face Tracking Guards** | Pauses gesture expressions while face tracking is active, and blink animations while eye tracking is active. Adds a **Disable Gestures** toggle to the avatar menu. Uses the same `FacialExpressionsDisabled` / `EyeTrackingActive` parameters as 1.x and Pawlygon Unity Tools, and drives `EyeTrackingActive` / `LipTrackingActive` for any animator that reads them, like 1.x did. |
| **Blendshape Fixes** | Drives fix shapes that prevent clipping, named after the shapes they fix (e.g. `MouthSmileLeft-EyeClosedLeft`, `LipFunnel-LipPucker`). |

### Upgrading from 1.x
The old OSC face tracking prefab can't be used together with native face tracking. When it's still on the avatar, the Face Tracking inspector shows it with a **Disable** button, and each build asks whether to remove it from that upload.

### Generated files
Builds save the assets they generate (FX copy, menu, parameters) in a `Packages/net.pawlygon.facetracking.temp` package in your project. It's safe to delete, and you can leave it out of version control.

## Legacy (1.x, OSC face tracking with VRCFaceTracking)
The 1.x prefabs are still included for avatars that keep using [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) over OSC. They need VRCFT v5 or newer, VRCFury or Modular Avatar, and the face mesh named `Body`. Add the prefab from `Packages/Pawlygon - VRC Facetracking/Deprecated/Prefabs` that matches your avatar's blendshapes (_"VF" is VRCFury and "MA" is Modular Avatar_).

## Support
Join our discord on [Pawlygon](https://discord.com/invite/pZew3JGpjb)

## Credits
* [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking)
* [regzo2 - OSCmooth](https://github.com/regzo2/OSCmooth)
* [Adjerry91 - FaceTracking Template](https://github.com/Adjerry91/VRCFaceTracking-Templates)
* [Hai-vr - fuck-osc-config](https://github.com/hai-vr/fuck-osc-config)
* [VRCFury](https://vrcfury.com) - inspiration for the non-destructive component approach

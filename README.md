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

The prefab contains these components. Each one shows what it will do in its inspector, and they're described in detail under [Components](#components):

| Component | What it does |
|---|---|
| **Face Tracking** | Sets up native face tracking on the avatar descriptor: meshes, blendshape mapping, visemes, eyelids and eye gaze. |
| **Face Tracking Parameters** | Keeps animators made for OSC face tracking working by feeding their `FT/v2/...` parameters from native face tracking. |
| **Face Tracking Guards** | Pauses gesture expressions and blink animations while face / eye tracking is active, with a **Disable Gestures** menu toggle. |
| **Blendshape Fixes** | Drives the fix shapes that prevent clipping when two shapes combine, like `MouthSmileLeft-EyeClosedLeft`. |
| **Pupil Emulation** | Lively pupils on headsets without pupil tracking, handing over to real pupil tracking when the headset has it. |
| **Face Tracking Emulation** | Fills in shapes some trackers don't send (e.g. brows from the eyelids), and relaxes shapes that fight each other. |

### Upgrading from 1.x
The old OSC face tracking prefab can't be used together with native face tracking. When it's still on the avatar, the Face Tracking inspector shows it with a **Disable** button, and each build asks whether to remove it from that upload.

### Generated files
Builds save the assets they generate (FX copy, menu, parameters) in a `Packages/net.pawlygon.facetracking.temp` package in your project. It's safe to delete, and you can leave it out of version control.

## Components

### Face Tracking
Sets up native face tracking on the avatar descriptor when the avatar is built: which meshes are tracked, which blendshape each tracking key drives, lip sync, eyelids and eye gaze. It replaces any face tracking setup already on the descriptor. The inspector shows how many tracking keys were mapped and lists the ones the avatar has no blendshape for.

| Setting | Default | What it does |
|---|---|---|
| Mesh Mode | Auto | **Auto** picks every mesh with face tracking blendshapes. **Manual** uses the Meshes list (up to 16). |
| All Tracking Keys | On | Maps every tracking key. Off limits the avatar to eye look and eyelids. Pupil Emulation's real pupil tracking and mood reactions, and Face Tracking Emulation, need it. |
| Lip Sync | Face Tracking Visemes | **Face Tracking Visemes** switches lip sync to the face tracking viseme mode, reusing the avatar's viseme blendshapes. **Keep Existing** leaves lip sync as it is. |
| Eyelids | Face Tracking | **Face Tracking** drives the eyelids with the `EyeClosed` blendshapes. **Keep Existing** leaves the descriptor's eyelid setup. |
| Eye Gaze | Auto | **Auto** uses **Both** with `EyeLook` blendshapes and eye bones set up in the descriptor's Eye Look, **Face Tracking** with only the blendshapes, **Bone Rotation** with only the bones, and leaves it unchanged with neither. |
| Viseme Dampening (Advanced) | None | How visemes and mouth tracking are blended: **Dampen Tracking** lets active visemes dampen mouth tracking, **Dampen Visemes** the other way round. Needs face tracking visemes and All Tracking Keys. |

Blendshapes are matched by their Unified Expressions names, and by the ARKit and other aliases the VRChat SDK knows.

### Face Tracking Parameters
Keeps animators made for OSC face tracking working, such as Pawlygon Unity Tools' Face Tracking Extras or your own custom animations: their face tracking parameters are fed from native face tracking instead of VRCFaceTracking.

| Setting | Default | What it does |
|---|---|---|
| Auto-detect | On | Forwards every known face tracking parameter (`FT/v2/...`, `OSCm/Proxy/FT/v2/...`) the avatar's animators use, including the controllers VRCFury and Modular Avatar merge. |
| Manual | Empty | Extra parameters to forward: an animator parameter and an expression of tracking keys, combined with `+ - * /`, `abs()`, `min()`, `max()` and `clamp()` (e.g. `JawRight - JawLeft`). The result is clamped to -1..1. An entry with the same name as a detected parameter replaces it. |

Each forwarded parameter counts toward the avatar's face tracking performance; the inspector shows a rank (Excellent up to 10, Good up to 20, Medium up to 30, Poor up to 40). No synced parameters are added.

### Face Tracking Guards
Stops hand gestures and blink animations in the FX controller from fighting face tracking. When the avatar is built, guards are added to a copy of the FX controller: gesture-driven expression layers pause while face tracking is on, and blink layers pause while eye tracking is on. The inspector lists the layers it found and lets you change which ones are guarded.

| Setting | Default | What it does |
|---|---|---|
| Gestures | On | Pauses gesture-driven facial expressions. |
| Disable With Face Tracking | On | Pauses gestures automatically while face tracking is active. |
| Disable Gestures Toggle | On | Adds a **Disable Gestures** menu toggle that pauses gestures whenever it's on, with or without face tracking. |
| In Menu | `Face Tracking` | Submenu of the avatar's main menu that gets the toggle. Created if missing. |
| Blinking | On | Pauses blink animations while eye tracking is active. |
| Layer lists | Empty | Expression layers left unguarded, gesture layers guarded anyway, and blink layers left out or added, by name. Set from the inspector's layer list. |

Guards use the same `FacialExpressionsDisabled` and `EyeTrackingActive` parameters as 1.x and Pawlygon Unity Tools, so FX controllers already guarded with Unity Tools keep working. They're set by the avatar's owner and synced, 1 bit each. `EyeTrackingActive` and `LipTrackingActive` are also driven whenever an animator reads them (e.g. Face Tracking Extras or 1.x custom animations), even with Blinking off.

### Blendshape Fixes
Drives fix shapes: helper blendshapes that prevent clipping while two face tracking shapes are active together, like `MouthSmileLeft-EyeClosedLeft`. Each fix follows the same tracking keys as the shapes it fixes, straight from face tracking, without animators.

| Setting | Default | What it does |
|---|---|---|
| Auto-detect | On | Drives every blendshape named after the face tracking shapes it fixes, joined with `-` (e.g. `MouthSmileLeft-EyeClosedLeft`, `LipFunnel-LipPucker`). |
| Combine | Average | How the fixed shapes are combined into the fix shape's value: **Average**, **Minimum**, **Multiply** or **Maximum**. |
| Excluded | Empty | Detected fixes left undriven, by blendshape name. |
| Manual | Empty | Extra fixes: a fix blendshape and the blendshapes it fixes, separated by commas. An entry with the same blendshape as a detected fix replaces it. |

### Pupil Emulation
Gives the pupils life on headsets without pupil tracking. While eye tracking is active, the pupils drift gently and react to every blink: they reopen wide, tighten, then settle. As soon as the headset reports real pupil dilation, that plays instead, per eye when the avatar has left/right pupil blendshapes, until eye tracking stops.

When the avatar is built, the `EyeDilation` / `EyeConstrict` blendshapes are taken over from native face tracking and driven by an FX layer. Remove the component to keep native pupil tracking only.

| Setting | Default | What it does |
|---|---|---|
| Idle Drift: Dilation Min / Max | 0 / 10 | Range of `EyeDilation` during the idle drift. |
| Blink Reflex: Peak Dilation | 70 | `EyeDilation` right after the eyes reopen from a blink. |
| Blink Reflex: Constriction | 50 | `EyeConstrict` at the tightest point of the reflex. |
| Blink Reflex: Duration | 1.8 s | Time for the reflex to settle back to the idle drift. |
| Blink Reflex: Closed Below / Reopened Above | 0.15 / 0.35 | Eyelid values (0 closed, 0.75 normally open) that count as a blink and as reopened. The gap stops noisy tracking from triggering false reflexes. |
| Mood Reactions | On | While the pupils are emulated, wide eyes constrict them and a frown on both sides dilates them. |
| Mood Reactions: Wide Eyes Constrict / Frown Dilation | 70 / 70 | How much each adds. |

Real pupil tracking and mood reactions need All Tracking Keys. The layer is gated by `EyeTrackingActive`, which is shared with Face Tracking Guards (1 synced bit), and adds a few forwarded parameters: the eyelids, one per pupil blendshape, and two for mood reactions.

Fake pupil layers avatars bring from 1.x, the one Face Tracking Extras generated or one made by hand, would fight Pupil Emulation over the pupil blendshapes, so they're emptied in the uploaded copy. A layer counts when it animates nothing but pupil blendshapes, reads face or eye tracking parameters, and doesn't react to hand gestures. The inspector lists them under **Replaced at Build**; your controllers aren't changed.

### Face Tracking Emulation
Fills in face tracking shapes from related ones, for trackers that don't send them (e.g. brows from the eyelids on eye-only trackers), and relaxes shapes that fight each other. Emulations only ever add to the tracked value, so they never override real tracking. When the avatar is built, each affected blendshape is driven by one native face tracking expression instead of its plain mapping, without animators or synced parameters. The inspector lists every rewritten blendshape; hover a row to see its expression.

Emulations, each with an amount:

| Emulation | Default | Amount | What it does |
|---|---|---|---|
| Tongue Opens Jaw | On | 0.25 | Sticking the tongue out opens the jaw a little. |
| Brows Up With Wide Eyes | On | 0.33 | Opening the eyes wide raises the brows. |
| Brows Down With Squint | On | 0.67 | Squinting lowers the brows. |
| Angry Brows | Off | 1 | Sneering or raising the chin lowers the brows. |
| Sad Brows | Off | 1 | Frowning with both mouth corners raises the inner brows, less while the mouth is closed. |

Limits:

| Limit | Default | What it does |
|---|---|---|
| Funnel Relaxed by Chin Raise | On | Raising the chin relaxes the lip funnel. |
| Raisers Relaxed by Closed Mouth | On | A closed mouth relaxes the upper and lower mouth raisers. |
| Tongue Pulled In by Funnel | On | Funneling the lips pulls the tongue back in. Avoids false tongue out on some trackers. |
| Lips Relaxed by Funnel | Off | Funneling relaxes the upper lip raise, lower lip down and pucker. |
| Jaw Reduced by Mouth Sideways | Off | Moving the mouth sideways reduces the jaw's sideways movement. |
| Frown Reduced by Mouth Sideways | Off | Moving the mouth sideways reduces the frown on that side. |
| Lip Suck Relaxed by Closed Mouth | Off | A closed mouth relaxes the lip suck. |

Rules whose blendshapes the avatar doesn't have are skipped. Needs All Tracking Keys. The expressions count toward VRChat's per-avatar face tracking expression budget; with every rule on it stays within it.

## Legacy (1.x, OSC face tracking with VRCFaceTracking)
The 1.x prefabs are still included for avatars that keep using [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) over OSC. They need VRCFT v5 or newer, VRCFury or Modular Avatar, and the face mesh named `Body`. Add the prefab from `Packages/Pawlygon - VRC Facetracking/Deprecated/Prefabs` that matches your avatar's blendshapes (_"VF" is VRCFury and "MA" is Modular Avatar_).

## Support
Join our discord on [Pawlygon](https://discord.com/invite/pZew3JGpjb)

## Credits
* [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking)
* [regzo2 - OSCmooth](https://github.com/regzo2/OSCmooth)
* [Adjerry91 - FaceTracking Template](https://github.com/Adjerry91/VRCFaceTracking-Templates) - Face Tracking Emulation's angry and sad brows and optional limits, and Pupil Emulation's mood reactions, follow the emulations and limits of his templates
* [Hai-vr - fuck-osc-config](https://github.com/hai-vr/fuck-osc-config)
* [VRCFury](https://vrcfury.com) - inspiration for the non-destructive component approach

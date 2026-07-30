# Nexus mod page assets

`description.bbcode` is the source of truth for the Nexus Mods description. Edit it here,
then paste it into the mod page's description field (the Nexus editor has a **BBCode**
toggle - paste into that, not the rich-text view, or the tags get escaped).

## Image placeholders

The file contains three `%%IMG_*%%` placeholders. Nexus cannot host an image from a
description alone, it has to exist in the mod page's **Images** tab first:

1. Mod page → **Images** → upload the screenshot.
2. Open the uploaded image, copy its **direct image URL**
   (`https://staticdelivery.nexusmods.com/mods/.../images/....jpg`).
3. Replace the placeholder with that URL.

| Placeholder | What to shoot |
|---|---|
| `%%IMG_HERO%%` | The thumbnail people judge the mod by. Mid-air over a gap with a horde behind you, or the moment of separation from a zombie's swing. Motion sells this mod - a static standing shot sells nothing. |
| `%%IMG_CONTROLS%%` | Options ▸ Controls ▸ Movement with "Dash" visible in the Player movement group, ideally with Jump and Crouch in frame so the reader sees it sitting among vanilla controls. This is the proof that it is a real, rebindable key and not a hardcoded hotkey - a question every reader will have. |
| `%%IMG_SETTINGS%%` | The Gears settings page with all six options visible. |

**A GIF or short video is worth more than all three.** This is a movement mod; the entire
value proposition is how it feels in motion, and no still frame conveys that. If you record
one, put it in the Videos tab - it becomes the page's strongest asset. Best single clip:
run at a gap, air dash across it, keep going.

## Audio provenance

Settled, recorded here so it does not have to be re-derived:

- `SevenDashesToDie/Resources/dash1.wav` is **ElevenLabs** output (text-to-sound-effect),
  generated on a **paid plan**, which covers commercial use and redistribution of the
  generated audio. Attribution to ElevenLabs is a free-tier requirement and does not apply.
- It is a sound effect, not speech: no voice, no likeness of any person, nothing cloned from
  a recording. That is the same position as BOOM HEADSHOT's clip, one step simpler.
- The file ships as delivered - PCM s16le, 48 kHz stereo, 0.48 s - with no re-encode, so
  there is no generation loss and nothing to reproduce.

**Tick the AI-generated-content flag when uploading.** Nexus asks at upload time, audio
counts, and the description says so anyway - a mismatch between the two is what gets a page
reported. The description's AI disclosure section carries the same statement.

## Notes

- Nexus renders on a **dark** background. The accent colour `#4fc3f7` is a light blue picked
  to stay readable there, and to read as "speed/motion" rather than as the warm orange used
  for BOOM HEADSHOT - the two mods should not look like the same page.
- The `━` separator lines are plain Unicode box-drawing characters, not a BBCode tag, so
  they render everywhere. Nexus has no reliable horizontal-rule tag.
- The `▸` in menu paths is likewise a plain character.
- Tags used: `size, b, i, color, center, url, img, list, list=1, quote`. All standard, but
  **use the Nexus preview before saving**: the exact tag support is not publicly documented.
- Keep this file in sync when defaults or settings change. It duplicates numbers from
  `SevenDashesToDie/ModSettings.xml` and `src/dll/SevenDashesMod.cs` on purpose, since the
  mod page cannot read them.

## Things the page deliberately says out loud

Recorded here so they do not get "cleaned up" later by someone who reads them as negatives:

- **No dash animation.** A reader who buys the mod and then notices this feels misled; a
  reader who is told up front reads the page as trustworthy. The game's rig has no such move
  and no mod can add one.
- **Removing the mod loses the spent skill points.** True of any perk mod, but the user is
  the one who eats it, so it belongs on the page rather than in a bug report.
- **Multiplayer replication is positional.** 7DTD sends `NetPackageEntityPosAndRot`; there is
  no velocity packet. Other players see interpolated position, so the dash is smoother for
  the dasher than for the spectator.

## Tags to set on the mod page

Gameplay, Player, Skills / Perks, Movement. The perk angle is what distinguishes this from
the "instant teleport" cheat mods it will otherwise be filed next to - lead with it in the
summary field too, not just in the body.

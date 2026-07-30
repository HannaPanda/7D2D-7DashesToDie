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
| `%%IMG_CONTROLS%%` | Options ▸ Controls with "Dash" visible in the Player Control list. This is the proof that it is a real, rebindable key and not a hardcoded hotkey - a question every reader will have. |
| `%%IMG_SETTINGS%%` | The Gears settings page with all six options visible. |

**A GIF or short video is worth more than all three.** This is a movement mod; the entire
value proposition is how it feels in motion, and no still frame conveys that. If you record
one, put it in the Videos tab - it becomes the page's strongest asset. Best single clip:
run at a gap, air dash across it, keep going.

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

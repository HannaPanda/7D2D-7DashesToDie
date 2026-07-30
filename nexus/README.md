# Nexus mod page assets

`description.bbcode` is the source of truth for the Nexus Mods description. Edit it here,
then paste it into the mod page's description field (the Nexus editor has a **BBCode**
toggle - paste into that, not the rich-text view, or the tags get escaped).

## Images

The description **hotlinks the screenshots straight out of this repo**, so a new one is a
commit and not a round trip through the Nexus Images tab:

```
https://raw.githubusercontent.com/HannaPanda/7D2D-7DashesToDie/refs/heads/main/nexus/images/<file>
```

| File | What it shows |
|---|---|
| `DashHero.jpg` | The thumbnail people judge the mod by. Motion sells this mod - a static standing shot sells nothing. |
| `DashKey.jpg` | Options ▸ Controls ▸ Movement with "Dash" in the Player movement group. The proof that it is a real, rebindable key and not a hardcoded hotkey - a question every reader will have. |
| `DashOptions.jpg` | The Gears settings page with all six options. |

**To swap an image:** drop the new file in `nexus/images/` under the same name, commit,
push. The description needs no edit and the mod page updates on its next load - GitHub sends
a short `max-age` on raw content, so it is a refresh, not a cache eviction.

Rules for what goes in this folder:

- **JPEG, max 1600 px wide, `-q:v 3`.** The originals are 1.7-1.9 MP PNGs at 2.3-2.8 MB
  each; hotlinked as-is the description would pull ~7.6 MB. Converted it is ~790 KB for all
  three. Conversion used:
  ```
  ffmpeg -y -i in.png -vf "scale='min(1600,iw)':-2" -q:v 3 out.jpg
  ```
- **Keep the filenames stable.** They are baked into the description; renaming one silently
  breaks a live mod page.
- The uncompressed originals are not kept here. Upload those to the Images tab (see below);
  this folder holds the web copies only.

**Two things this does not replace:**

1. **Still upload the screenshots to the mod page's Images tab.** The gallery, the thumbnail
   and the search preview all come from there, not from the description. The hotlinking only
   saves the copy-the-CDN-URL step for images embedded *in the body*.
2. **Nexus may not permit external image hosts in a description.** This setup is deliberately
   an experiment. If the images come out broken or stripped in the Nexus preview, fall back
   to uploading them and pasting the resulting
   `https://staticdelivery.nexusmods.com/mods/.../images/....jpg` URLs - the layout is
   unchanged, only the three URLs differ. Check the preview before saving.

**A GIF or short video is worth more than all three stills.** This is a movement mod; the
entire value proposition is how it feels in motion, and no still frame conveys that. If you
record one, put it in the Videos tab - it becomes the page's strongest asset. Best single
clip: run at a gap, air dash across it, keep going.

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

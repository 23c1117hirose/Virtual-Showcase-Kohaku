Frog sounds used by the poke task (PokeTaskManager)

Land/    landing of the frog (one of the clips is picked at random each time)
Rustle/  grass rustle when the frog leaves the grass / dives back in

Both folders are loaded with Resources.LoadAll, so more WAV files can simply be dropped in (or removed).
If a folder is empty, a generated sound is used instead (FrogSoundSynth).

Origin: made from two free sound files found and chosen by the author:
  Land/    "雨で濡れた道路を歩く.mp3" (walking on a wet road in the rain): single steps cut out,
           high-passed at 180 Hz, pitched up by 15 %, faded, levelled with a soft limiter
  Rustle/  "草むらを歩く.mp3" (walking through grass): short swishes cut out, high-passed at 600 Hz,
           pitched up by 15 %, faded, levelled
The processing script is blender/make_frog_sounds.py (run with Blender: see the header of the script).
Original site and licence of the two MP3 files: TO BE FILLED IN (needed for the thesis / publication).

"""Construit FM27ScoutApp/payload/bepinex.zip (contenu installé dans le jeu par FM27-Assistant-Setup.exe).

Usage : python scripts/make-payload.py "D:\\...\\steamapps\\common\\Football Manager 27"
Le dossier du jeu doit contenir BepInEx 6 (IL2CPP, bleeding edge) déjà lancé une fois, et FM27 Bridge compilé
(BepInEx\\plugins\\FM27Bridge\\FM27Bridge.dll). Les dossiers générés (interop, cache, unity-libs) ne sont pas inclus :
BepInEx les recrée au premier lancement du jeu.
"""
import os, sys, zipfile

game = sys.argv[1]
out = os.path.join(os.path.dirname(__file__), "..", "FM27ScoutApp", "payload", "bepinex.zip")
os.makedirs(os.path.dirname(out), exist_ok=True)
files = ["winhttp.dll", ".doorstop_version", "doorstop_config.ini", "BepInEx/config/BepInEx.cfg", "BepInEx/plugins/FM27Bridge/FM27Bridge.dll"]
dirs = ["dotnet", "BepInEx/core"]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for f in files:
        z.write(os.path.join(game, f), f)
    for d in dirs:
        for dp, _, fn in os.walk(os.path.join(game, d)):
            for f in fn:
                full = os.path.join(dp, f)
                z.write(full, os.path.relpath(full, game).replace("\\", "/"))
print("OK :", out, os.path.getsize(out) // 1048576, "Mo")

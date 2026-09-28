"""Полная пересборка аксессуаров Финика.

    blender --background --python art/blender/scripts/build_all.py
    blender --background --python art/blender/scripts/build_all.py -- --no-render

1. импорт персонажа (Idle-FBX из Unity-проекта), rest-поза;
2. процедурная сборка 16 аксессуаров по замерам тела + отчёт о пересечениях;
3. экспорт FBX и accessories_manifest.json в unity/Finik/Assets/Finik/Accessories;
4. витрины в docs/accessories;
5. сохранение art/blender/finik_accessories.blend.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import finik_scene as scene      # noqa: E402
import finik_build as build      # noqa: E402

OUTFITS = {
    "outfit_front": (["Gear_Cap", "Glasses_Nerd", "Backpack_School", "Gear_Watch", "Gear_Badge"], 24),
    "outfit_back": (["Gear_Cap", "Glasses_Nerd", "Backpack_School", "Gear_Watch", "Gear_Badge"], 205),
    "outfit_winter": (["Gear_Beanie", "Glasses_Round", "Gear_Scarf", "Backpack_FoxMini"], 28),
    "outfit_sport": (["Gear_Headphones", "Glasses_Visor", "Backpack_RollTop", "Gear_Watch"], -26),
}


def main(render=True):
    scene.import_character()
    scene.lights()
    build.rebuild(report=True)
    build.export(os.path.join(scene.UNITY_ACCESSORIES, "Models"),
                 os.path.join(scene.UNITY_ACCESSORIES, "accessories_manifest.json"))
    if render:
        import finik_render as render_mod
        out = scene.SHOWCASE_DIR
        render_mod.render_sheet("Glasses_", os.path.join(out, "glasses.png"), cols=3, yaw=20, pitch=-10, gap=0.14)
        render_mod.render_sheet("Backpack_", os.path.join(out, "backpacks.png"), cols=4, yaw=205, pitch=-12, gap=0.14)
        render_mod.render_sheet("Gear_", os.path.join(out, "gear.png"), cols=3, yaw=22, pitch=-10, gap=0.14)
        for name, (items, yaw) in OUTFITS.items():
            render_mod.render_outfit(items, os.path.join(out, name + ".png"), yaw=yaw)
    print("saved:", scene.save())


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(render="--no-render" not in argv)

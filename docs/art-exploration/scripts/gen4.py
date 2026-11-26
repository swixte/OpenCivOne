import sys
from concurrent.futures import ThreadPoolExecutor
import gen
from gen2 import A_STYLE, ANGLE
from gen3 import KEY

FLAT = ("completely flat colour areas, no soft shading, no gradients, no highlights, no glossy or 3D rendering, strong even black keyline, "
        "slight visible woodgrain in the flat colour, slight print registration offset. Not modern game art, not a 3D render, not a digital painting. ")
YOKO = ("A Yokohama-e Japanese woodblock print of the 1860s in the manner of Utagawa Sadahide, drawn the way a Japanese printmaker of that time imagined "
        "Westerners and ancient Romans: " + FLAT + "Limited palette of indigo, Prussian blue, cinnabar red, ochre and cream. ")
MEIJI = ("A Meiji-era Japanese woodblock print, a senso-e war print or kaika-e enlightenment print in the manner of Kobayashi Kiyochika and the "
         "Russo-Japanese war triptychs, imagining modern machines the way a printmaker of that time would: " + FLAT +
         "Palette of Prussian blue, aniline red, ochre, slate grey, black and cream. ")
ASIA = A_STYLE
COMPACT = " The whole city forms one compact block, roughly as wide as it is deep, fully visible."
BUST = (" Head and shoulders, centered, on plain cream paper. No text, no seals, no signature, no cartouche, no border.")


def unit(style, subj, part):
    return style + "The subject: " + subj + ". " + KEY.format(part=part) + ANGLE


def obj(style, subj):
    return style + "The subject: " + subj + ". " + ANGLE


JOBS = {
    # Roman, ancient (Yokohama-e)
    'R-legion': unit(YOKO, "a Roman legionary standing upright facing forward, with a tall curved rectangular red scutum shield, a short sword, a crested helmet, "
                           "and beside him a tall standard pole topped with a golden eagle and laurel wreaths, with a small square vexillum banner hanging from it", 'small square vexillum banner on the standard'),
    'R-cavalry': unit(YOKO, "a Roman cavalryman on a galloping horse, crested helmet, holding upright a long spear with a small square vexillum banner tied below its tip", 'vexillum banner on the spear'),
    'R-settler': unit(YOKO, "a low, wide two-wheeled Roman ox cart loaded with amphorae and sacks, pulled by an ox, a settler in a tunic walking at the ox's head; "
                            "the group is much wider than tall, with a small square cloth flag on a short pole stuck into the load", 'small square cloth flag on the cart'),
    'R-trireme': unit(YOKO, "a Roman trireme warship with a bronze ram, three banks of oars, a curved stern and one big square sail with a large round disc in the middle", 'round disc in the middle of the sail'),
    'R-city': obj(YOKO, "a walled Roman town: pale stone walls with square towers and an arched gate, inside them a temple with columns and a pediment, "
                        "terracotta-roofed houses and a tall tower." + COMPACT),
    'R-town': obj(YOKO, "a small Roman town: a few houses with terracotta roofs around a small columned temple, inside a low stone wall." + COMPACT),
    'R-portrait': YOKO + "A bust portrait of Julius Caesar with a laurel wreath, a stern face and a white toga with a red border." + BUST,
    # Western, modern (Meiji)
    'W-armor': unit(MEIJI, "a battle tank with a turret, a long gun and tracks, with a small square pennant flag on its whip antenna", 'pennant flag on the antenna'),
    'W-mech': unit(MEIJI, "a mechanized infantry carrier: a boxy tracked armoured vehicle with three helmeted soldiers with rifles riding on top, with a small square pennant on its antenna", 'pennant on the antenna'),
    'W-artillery': unit(MEIJI, "a big field artillery gun with a long barrel on a two-wheeled carriage, two gunners in uniform beside it, and a small square flag on a short pole", 'small square flag'),
    'W-bomber': unit(MEIJI, "a propeller bomber aeroplane in flight, seen from high above at an angle so both long wings show, with a large round roundel on each wing", 'round roundel on each wing'),
    'W-battleship': unit(MEIJI, "a steel battleship with two big gun turrets and two tall funnels trailing stylized smoke, with a flag on the stern mast", 'flag on the stern mast'),
    'W-transport': unit(MEIJI, "a steam cargo transport ship with one funnel, cargo cranes and crates on deck, with a flag on the mast", 'flag on the mast'),
    'W-city': obj(MEIJI, "a dense industrial city: red brick buildings, factories with tall smoking chimneys, a clock tower, an iron bridge, a railway station, gas lamps and telegraph poles." + COMPACT),
    'W-city2': obj(MEIJI, "a modern city as a Meiji printmaker would imagine the far future: tall stepped skyscrapers, a domed civic hall, "
                          "a nuclear power plant cooling tower with a plume of steam, and two searchlight beams." + COMPACT),
    'W-pollution': obj(MEIJI, "a patch of industrial pollution on the ground: a low dark blot of black and brown oily smog with scattered debris and a dead tree, low and flat, much wider than tall"),
    'W-portrait': MEIJI + "A bust portrait of a Western statesman of about 1900 with a moustache, a black frock coat, a white collar and a red sash with a medal." + BUST,
    # eras sheet
    'E-asia-ancient-city': obj(ASIA, "an ancient Chinese walled town of the Han dynasty: rammed-earth walls with a tall gate tower with curved roofs, houses with grey tile roofs inside." + COMPACT),
    'E-asia-ancient-unit': unit(ASIA, "an ancient Chinese spearman of the Qin dynasty standing upright, in lamellar armour, with a tall ji halberd and a small square banner on a pole on his back", 'small square banner on his back'),
    'E-asia-ind-city': obj(MEIJI, "a Meiji-era Tokyo block: red brick Western-style buildings with a clock tower, a Japanese temple roof, gas lamps, and a small steam train on an iron bridge." + COMPACT),
    'E-asia-ind-unit': unit(MEIJI, "a Meiji Japanese infantry rifleman standing upright in a dark blue uniform and kepi, with a rifle and bayonet, beside a small square flag on a pole", 'small square flag'),
    'E-asia-mod-city': obj(MEIJI, "a modern Asian city as a Meiji printmaker would imagine the future: tall towers with stepped pagoda-like roofs, flat red sign shapes, an elevated railway on pillars." + COMPACT),
    'E-asia-mod-unit': unit(ASIA, "a modern battle tank with a turret and long gun, its armour decorated with Hokusai wave patterns, with a small square pennant on its antenna", 'pennant on the antenna'),
    'E-west-med-city': obj(YOKO, "a medieval European castle town: stone curtain walls with round towers, a Gothic cathedral with spires, half-timbered houses." + COMPACT),
    'E-west-med-unit': unit(YOKO, "a European medieval knight in plate armour on an armoured horse, holding upright a lance with a square heraldic banner", 'square banner on the lance'),
    'E-west-ind-city': obj(YOKO, "a Western port town block of 1860s Yokohama: two-storey Western brick and stucco houses with verandas, a church steeple, a tall flagpole." + COMPACT),
    'E-west-ind-unit': unit(YOKO, "a Western rifleman of the 1860s standing upright, dark blue coat, white trousers, tall shako hat, rifle on his shoulder, beside a small square flag on a pole", 'small square flag'),
}


def jobs():
    for k, p in JOBS.items():
        yield f'ukiyo-e-{k}', p, '1024x1024', 'medium'


if __name__ == '__main__':
    sel = sys.argv[1:]
    js = [j for j in jobs() if not sel or any(j[0].startswith('ukiyo-e-' + p) for p in sel)]
    with ThreadPoolExecutor(10) as ex:
        for name, status in ex.map(gen.gen, js):
            print(name, status, flush=True)

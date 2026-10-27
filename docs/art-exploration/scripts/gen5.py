import sys
from concurrent.futures import ThreadPoolExecutor
import gen
from gen2 import A_STYLE, ANGLE

TOP = ("Seen straight down from directly above like a map, orthographic, no horizon, no perspective. "
       "The pattern fills the entire image evenly edge to edge, no border, no frame, no buildings, no people, no trees, no animals, no text, no seals. ")
JOBS = {
    'T-paddy': ("A Japanese ukiyo-e woodblock print in the manner of Hiroshige: a dense patchwork of small flooded rice paddies, about 16 by 16 plots across the whole image, "
                "rectangular and slightly irregular, separated by narrow raised earthen bund paths drawn with fine black keyline edges. "
                "Most paddies hold flat sky-blue water with pale bokashi reflections, some are planted with neat rows of young green rice shoots, a few are ripe gold. "
                "Flat colour, black keyline, visible woodgrain, cream paper. " + TOP, '1024x1024'),
    'T-fields': ("A Yokohama-e Japanese woodblock print of the 1860s showing Roman centuriation farmland: a regular grid of square fields, about 16 by 16 across the whole image, "
                 "each ploughed into parallel furrow strips, alternating direction from field to field, in ochre, wheat gold and fresh green; some fields are vineyards of dark green dot rows, "
                 "some olive groves of round grey-green dots in rows; the field boundaries are narrow pale paths with fine black keyline, with small blue irrigation ditches along some of them. "
                 "Flat colour, black keyline, visible woodgrain. " + TOP, '1024x1024'),
    'M-mine2': (A_STYLE + "The subject: a small mine cut into a grassy hillside: a dark adit tunnel entrance with a heavy timber frame set into a slope of earth and grey rock, "
                "two low spoil heaps of grey stones spilling down the slope, a small ore cart on a short wooden track. Low and compact, much wider than tall; "
                "the grassy slope fades out at its edges. " + ANGLE, '1024x1024'),
    'M-hut2': (A_STYLE + "The subject: a small cluster of two thatched peasant huts and a straw-roofed storehouse standing on a patch of trodden earth, low and compact. " + ANGLE, '1024x1024'),
}


def jobs():
    for k, (p, size) in JOBS.items():
        yield f'ukiyo-e-{k}', p, size, 'medium'


if __name__ == '__main__':
    sel = sys.argv[1:]
    js = [j for j in jobs() if not sel or any(j[0].startswith('ukiyo-e-' + p) for p in sel)]
    with ThreadPoolExecutor(6) as ex:
        for name, status in ex.map(gen.gen, js):
            print(name, status, flush=True)

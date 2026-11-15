/** Champs du volet « Seuils » : libellé, bornes, pas, décimales, suffixe, infobulle (textes de l'original). */
export const THRESHOLD_FIELDS_UI = [
  {
    key: 'hissDb',
    label: 'Souffle au-dessus de',
    min: -90,
    max: -30,
    step: 1,
    decimals: 0,
    suffix: 'dB',
    tip: "Plancher de bruit mesuré au-dessus de 8 kHz. Un titre sain\nreste sous −80 dB ; un souffle audible remonte ce plancher\nà son propre niveau.\nUn morceau que rien n'interrompt n'a pas de plancher à\nmesurer : il n'est signalé que si le bruit relevé est bien\nlarge bande, ce que la colonne « Souffle » explique au survol.",
  },
  {
    key: 'cracklePct',
    label: 'Craquements au-delà de',
    min: 0.0005,
    max: 1,
    step: 0.0005,
    decimals: 4,
    suffix: '%',
    tip: "Part d'échantillons imprévisibles. Titres sains : moins de\n0,001 % ; deux craquements par seconde : 0,004 %.",
  },
  {
    key: 'clippingPct',
    label: 'Saturation au-delà de',
    min: 0.005,
    max: 5,
    step: 0.005,
    decimals: 3,
    suffix: '%',
    tip: "Part d'échantillons collés à la butée du format. Une variété\nrécente en montre couramment 0,03 à 0,08 % : c'est le master\nqui est ainsi, retélécharger n'y changerait rien. Au-delà de\n0,2 %, la distorsion s'entend.",
  },
  {
    key: 'gapDensityPct',
    label: 'Coupures occupant plus de',
    min: 5,
    max: 90,
    step: 5,
    decimals: 0,
    suffix: '%',
    tip: "Part de silence numérique — des échantillons nuls, non du signal\nfaible — du passage de dix secondes le plus troué.\nLes compter ne suffirait pas : « We Will Rock You » montre\nsoixante-neuf silences, ceux qui séparent ses frappes de pied,\net se porte très bien. Ce qui trahit une extraction qui hoquette,\nc'est qu'elle blanchit une large part d'un passage entier :\n51 % là où un fichier sain ne dépasse pas 19 %, et reste le plus\nsouvent sous 5 %.\nLes blancs de début et de fin sont exclus : ils ont leur propre\nseuil.",
  },
  {
    key: 'tailSeconds',
    label: 'Blanc de fin au-delà de',
    min: 1,
    max: 60,
    step: 1,
    decimals: 0,
    suffix: 's',
    tip: "Durée de la plage muette terminant le fichier. Une fin en\nfondu en occupe deux ou trois.\nUn blanc n'est signalé que si le fichier dépasse en outre la\ndurée du morceau (voir le seuil ci-dessous).",
  },
  {
    key: 'headSeconds',
    label: 'Blanc de début au-delà de',
    min: 0.5,
    max: 30,
    step: 0.5,
    decimals: 1,
    suffix: 's',
    tip: "Durée de la plage muette ouvrant le fichier.\nUn blanc n'est signalé que si le fichier dépasse en outre la\ndurée du morceau (voir le seuil ci-dessous).",
  },
  {
    key: 'durationGap',
    label: "Durée s'écartant de la référence de plus de",
    min: 2,
    max: 120,
    step: 1,
    decimals: 0,
    suffix: 's',
    tip: "Écart, dans un sens ou dans l'autre, entre la durée du fichier\net celle du morceau chez Deezer ou MusicBrainz.\nUn fichier plus court ou plus long n'est pas la version\nattendue : montage écourté, édition radio, extraction d'une\nmauvaise source. C'est souvent le seul indice exploitable — un\nrip radio propre se mesure comme un master propre.\nSur soixante-dix titres tirés au hasard, l'écart médian est de\n1,6 s et les trois quarts tiennent sous 3,4 s ; au-delà de dix\nsecondes commence une autre famille.",
  },
  {
    key: 'durationExcess',
    label: 'Blancs signalés si la durée dépasse de',
    min: 1,
    max: 60,
    step: 1,
    decimals: 0,
    suffix: 's',
    tip: "Un silence n'est un défaut que s'il a été ajouté : beaucoup de\nmorceaux se terminent sur un fondu ou des applaudissements que\nleur master contient bel et bien.\nLa durée du morceau est cherchée chez Deezer, puis chez\nMusicBrainz — et la plus proche de celle du fichier est retenue,\npour ne pas accuser un titre de ne pas durer autant qu'un remix.\nSur des fichiers sains, l'écart mesuré ne dépasse pas 3 s.\nSans durée de référence, aucun blanc n'est signalé.",
  },
];

export const DEEP_TIP =
  "Les craquements se repèrent en prédisant chaque échantillon à\npartir des précédents : c'est la mesure la plus sûre, et de loin\nla plus coûteuse. Décochez-la pour une analyse rapide limitée au\nsouffle, à la saturation et aux blancs.";

/**
 * Nouvelle valeur d'un seuil saisie au clavier, ramenée dans ses bornes ; null si la saisie n'est pas un nombre
 * (le champ garde alors la valeur retenue).
 */
export function withThreshold(current, field, raw) {
  const value = parseFloat(raw);
  if (!current || !Number.isFinite(value)) return null;
  return { ...current, [field.key]: Math.min(field.max, Math.max(field.min, value)) };
}

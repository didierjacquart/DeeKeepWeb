import { useState } from 'react';

/**
 * Jaquette : la première des sources qui se charge, sinon une croche.
 * Le parent passe `key={sources.join('|')}` pour repartir de la première à chaque titre.
 * @param {{ sources: string[], className?: string }} props
 */
export function Cover({ sources, className }) {
  const [failed, setFailed] = useState(0);
  const src = sources[failed];
  return src ? (
    <img className={className} src={src} alt="" onError={() => setFailed(n => n + 1)} />
  ) : (
    <div className={className ? `${className} placeholder` : 'placeholder'}>♪</div>
  );
}

import { act, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithRouter } from '../../../test/render';
import { player } from '../player';
import { offlineAudio } from '../offline-audio';
import { PlayerBar } from './PlayerBar';

const queue = [
  {
    id: 'a',
    title: 'One More Time',
    artist: 'Daft Punk',
    album: 'Discovery',
    src: '/media/track?path=a',
    duration: 320,
  },
  { id: 'b', title: 'Aerodynamic', artist: 'Daft Punk', src: '/media/track?path=b', duration: 212 },
];
const flush = () => act(() => new Promise(r => setTimeout(r, 0)));

describe('PlayerBar', () => {
  beforeEach(() => vi.spyOn(offlineAudio, 'objectUrl').mockResolvedValue(null));
  afterEach(() => act(() => player.close()));

  it('reste invisible tant que rien ne joue', () => {
    const { container } = renderWithRouter(<PlayerBar />);
    expect(container.ownerDocument.querySelector('.dk-player-bar')).not.toHaveClass('visible');
    expect(document.documentElement.style.getPropertyValue('--dk-player-height')).toBe('0px');
  });

  it('montre le titre joué et se commande', async () => {
    const { user } = renderWithRouter(<PlayerBar />);
    act(() => player.play(queue[0], queue));
    await flush();
    expect(screen.getByText('One More Time')).toBeInTheDocument();
    expect(document.documentElement.style.getPropertyValue('--dk-player-height')).toBe('72px');
    await user.click(screen.getByRole('button', { name: 'Pause (Espace)' }));
    expect(player.state.playing).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Suivant' }));
    await flush();
    expect(screen.getByText('Aerodynamic')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Suivant' })).toBeDisabled();
  });

  it('ouvre la fenêtre de lecture avec la jaquette du fichier et la file d’attente', async () => {
    const { user } = renderWithRouter(<PlayerBar />);
    act(() => player.play(queue[0], queue));
    await flush();
    await user.click(screen.getByRole('button', { name: 'Ouvrir la fenêtre du lecteur' }));
    const panel = screen.getByRole('region', { name: 'Lecteur' });
    expect(within(panel).getByText('Lecteur DeeKeep')).toBeInTheDocument();
    expect(within(panel).getByText('Titre 1 / 2')).toBeInTheDocument();
    expect(panel.querySelector('img.pw-cover')).toHaveAttribute('src', '/media/cover?path=a');
    expect(within(panel).getByRole('img', { name: "Égaliseur d'analyse" })).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Arrêter' }));
    expect(player.state.playing).toBe(false);
    await user.click(within(panel).getByRole('button', { name: /File d'attente/ }));
    const list = within(panel).getByRole('list', { name: "File d'attente" });
    await user.click(within(list).getByRole('button', { name: /Aerodynamic/ }));
    await flush();
    expect(player.current.id).toBe('b');
    await user.click(within(panel).getByRole('button', { name: 'Replier' }));
    expect(screen.queryByRole('region', { name: 'Lecteur' })).not.toBeInTheDocument();
  });

  it('se positionne au clavier sur la rainure et règle le volume', async () => {
    const { user } = renderWithRouter(<PlayerBar />);
    act(() => player.play(queue[0], queue));
    await flush();
    const seek = vi.spyOn(player, 'seekBy');
    screen.getByRole('slider', { name: 'Position de lecture' }).focus();
    await user.keyboard('{ArrowRight}');
    expect(seek).toHaveBeenCalledWith(5);
    const volume = screen.getByRole('slider', { name: 'Volume' });
    act(() => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(volume, '50');
      volume.dispatchEvent(new Event('input', { bubbles: true }));
    });
    expect(player.state.volume).toBe(0.5);
  });

  it('signale dans la barre un flux refusé et repasse en pause', async () => {
    let element;
    const play = HTMLMediaElement.prototype.play;
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function () {
      element = this;
      return play.call(this);
    });
    renderWithRouter(<PlayerBar />);
    const archived = { ...queue[0], src: '/archive-media/track?path=a' };
    act(() => player.play(archived, [archived]));
    await flush();
    act(() => void element.dispatchEvent(new Event('error')));
    expect(screen.getByRole('alert')).toHaveTextContent('archive injoignable');
    expect(player.state.playing).toBe(false);
    expect(screen.getByRole('button', { name: /Lecture/ })).toBeInTheDocument();
  });

  it('se ferme', async () => {
    const { user } = renderWithRouter(<PlayerBar />);
    act(() => player.play(queue[0], queue));
    await flush();
    await user.click(screen.getByRole('button', { name: 'Fermer le lecteur' }));
    expect(screen.queryByText('One More Time')).not.toBeInTheDocument();
  });
});

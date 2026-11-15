import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PlayerService } from './player';
import { offlineAudio } from './offline-audio';

const track = (id, fields = {}) => ({
  id,
  title: `Titre ${id}`,
  artist: 'Artiste',
  src: `/media/track?path=${id}`,
  ...fields,
});
const queue = ['a', 'b', 'c'].map(id => track(id));
const flush = () => new Promise(r => setTimeout(r, 0));

describe('PlayerService', () => {
  let player;

  beforeEach(() => {
    vi.spyOn(offlineAudio, 'objectUrl').mockResolvedValue(null);
    player = new PlayerService();
  });

  it('prend la liste affichée comme file et joue le titre cliqué', async () => {
    player.play(queue[1], queue);
    await flush();
    expect(player.state).toMatchObject({ index: 1, playing: true, queue });
    expect(player.current.id).toBe('b');
    expect(player.hasNext).toBe(true);
    expect(player.hasPrevious).toBe(true);
  });

  it('joue un titre seul quand il n’y a pas de liste', async () => {
    player.play(track('x'));
    await flush();
    expect(player.state.queue.map(t => t.id)).toEqual(['x']);
    expect(player.hasNext).toBe(false);
  });

  it('avance et recule dans la file', async () => {
    player.play(queue[0], queue);
    await flush();
    player.next();
    await flush();
    expect(player.current.id).toBe('b');
    player.previous();
    await flush();
    expect(player.current.id).toBe('a');
  });

  it('met en pause et reprend', async () => {
    player.play(queue[0], queue);
    await flush();
    player.toggle();
    expect(player.state.playing).toBe(false);
    player.toggle();
    expect(player.state.playing).toBe(true);
  });

  it('un extrait suspend le lecteur, qui reprend à la fin de l’extrait', async () => {
    player.play(queue[0], queue);
    await flush();
    const preview = track('deezer:1', { src: '/preview/1' });
    player.togglePreview(preview);
    expect(player.state).toMatchObject({ playing: false, pausedByPreview: true, previewPlaying: true });
    player.togglePreview(preview); // second clic : arrêt
    await flush();
    expect(player.state).toMatchObject({ preview: null, previewPlaying: false, pausedByPreview: false, playing: true });
  });

  it('borne et mémorise le volume', () => {
    player.setVolume(1.4);
    expect(player.state.volume).toBe(1);
    player.setVolume(0.5);
    expect(localStorage.getItem('dk-player-volume')).toBe('0.5');
    expect(new PlayerService().state.volume).toBe(0.5);
  });

  it('lit un titre épinglé depuis l’appareil', async () => {
    offlineAudio.objectUrl.mockResolvedValue('blob:local');
    const created = [];
    const OriginalAudio = window.Audio;
    vi.spyOn(window, 'Audio').mockImplementation(function Audio() {
      const a = new OriginalAudio();
      created.push(a);
      return a;
    });
    player.play(queue[0], queue);
    await flush();
    expect(created[0].src).toBe('blob:local');
  });

  it('se ferme en vidant la file', async () => {
    player.play(queue[0], queue);
    await flush();
    player.setExpanded(true);
    player.close();
    expect(player.state).toMatchObject({ queue: [], index: -1, expanded: false });
    expect(player.current).toBeNull();
  });

  it('répond aux raccourcis clavier hors des champs de saisie', async () => {
    player.play(queue[0], queue);
    await flush();
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', bubbles: true }));
    await flush();
    expect(player.current.id).toBe('b');
    const input = document.createElement('input');
    document.body.append(input);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', bubbles: true }));
    await flush();
    expect(player.current.id).toBe('b');
    input.remove();
  });
});

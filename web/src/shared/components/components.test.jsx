import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProgressBar } from './ProgressBar';
import { PresenceBadge } from './PresenceBadge';
import { TooltipHost } from '../tooltip/TooltipHost';
import { VirtualList } from '../virtual-list/VirtualList';
import { Toaster } from '../../core/ui/Toaster';
import { notify } from '../../core/ui/notify';

describe('ProgressBar', () => {
  it('affiche le pourcentage par défaut', () => {
    render(<ProgressBar value={0.42} />);
    const bar = screen.getByRole('progressbar');
    expect(bar).toHaveAttribute('aria-valuenow', '42');
    expect(bar).toHaveTextContent('42 %');
    expect(bar.querySelector('.fill')).toHaveStyle({ width: '42%' });
  });

  it('affiche un libellé, ou rien, et sait être indéterminée', () => {
    const { rerender } = render(<ProgressBar value={0.5} label="3:20 - 50%" large />);
    expect(screen.getByRole('progressbar')).toHaveTextContent('3:20 - 50%');
    expect(screen.getByRole('progressbar')).toHaveClass('large');
    rerender(<ProgressBar value={null} label={null} />);
    expect(screen.getByRole('progressbar')).toHaveClass('indeterminate');
    expect(screen.getByRole('progressbar')).toHaveTextContent('');
  });
});

describe('PresenceBadge', () => {
  it.each([
    ['PRESENT', '✓', 'present'],
    ['POSSIBLE', '≈', 'possible'],
    ['ABSENT', '○', 'absent'],
    [null, '⏳', 'pending'],
  ])('%s → %s', (state, glyph, cls) => {
    render(<PresenceBadge state={state} tooltip={'D:\\Pop\\a.mp3'} />);
    const badge = screen.getByRole('img');
    expect(badge).toHaveTextContent(glyph);
    expect(badge).toHaveClass(cls);
    expect(badge).toHaveAttribute('data-tip', 'D:\\Pop\\a.mp3');
  });
});

describe('TooltipHost', () => {
  afterEach(() => vi.useRealTimers());

  it('montre le texte de data-tip au survol, puis le cache', async () => {
    const user = userEvent.setup();
    render(
      <>
        <TooltipHost />
        <button data-tip={'Ligne 1\nLigne 2'}>Aide</button>
        <p>Ailleurs</p>
      </>,
    );
    await user.hover(screen.getByRole('button', { name: 'Aide' }));
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Ligne 1 Ligne 2');
    await user.hover(screen.getByText('Ailleurs'));
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });
});

describe('VirtualList', () => {
  it('ne dessine que les lignes visibles et suit le défilement', () => {
    vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(200);
    const items = Array.from({ length: 1000 }, (_, i) => `Titre ${i}`);
    const { container } = render(
      <VirtualList
        items={items}
        itemHeight={40}
        buffer={0}
        itemKey={t => t}
        renderItem={t => <div role="row">{t}</div>}
      />,
    );
    expect(screen.getAllByRole('row').map(r => r.textContent)).toEqual([
      'Titre 0',
      'Titre 1',
      'Titre 2',
      'Titre 3',
      'Titre 4',
    ]);
    const viewport = container.querySelector('.dk-viewport');
    act(() => {
      viewport.scrollTop = 4000;
      viewport.dispatchEvent(new Event('scroll'));
    });
    expect(screen.getAllByRole('row')[0]).toHaveTextContent('Titre 100');
    expect(viewport.firstChild).toHaveStyle({ height: '40000px' });
  });
});

describe('Toaster', () => {
  it('affiche le dernier message et se ferme par son bouton', async () => {
    const user = userEvent.setup();
    render(<Toaster />);
    act(() => notify.info('Chemin copié.'));
    act(() => notify.error('Copie impossible.'));
    expect(screen.getByRole('alert')).toHaveTextContent('Copie impossible.');
    await user.click(screen.getByRole('button', { name: 'Fermer' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('disparaît seul après quelques secondes', () => {
    vi.useFakeTimers();
    render(<Toaster />);
    act(() => notify.success('Enregistré.'));
    expect(screen.getByRole('status')).toHaveTextContent('Enregistré.');
    act(() => vi.advanceTimersByTime(3000));
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    vi.useRealTimers();
  });
});

import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { DialogHost } from './DialogHost';
import { DialogActions, DialogContent, DialogTitle } from './DialogParts';
import { openDialog } from './dialog-stack';
import { confirm } from './confirm';

function Hello({ close }) {
  return (
    <>
      <DialogTitle>Bonjour</DialogTitle>
      <DialogContent>
        <input aria-label="Nom" />
      </DialogContent>
      <DialogActions>
        <button onClick={() => close('ok')}>Valider</button>
      </DialogActions>
    </>
  );
}

function open(options) {
  let result;
  act(() => {
    result = openDialog(close => <Hello close={close} />, options);
  });
  return result;
}

describe('openDialog', () => {
  it('rend le résultat passé à close et referme la boîte', async () => {
    const user = userEvent.setup();
    render(<DialogHost />);
    const result = open();
    expect(screen.getByRole('dialog')).toHaveTextContent('Bonjour');
    await user.click(screen.getByRole('button', { name: 'Valider' }));
    await expect(result).resolves.toBe('ok');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('donne le focus au premier champ, ou à personne si autoFocus est faux', () => {
    render(<DialogHost />);
    open();
    expect(screen.getByLabelText('Nom')).toHaveFocus();
    act(() => screen.getByRole('button', { name: 'Valider' }).click());
    open({ autoFocus: false });
    expect(screen.getByRole('dialog')).toHaveFocus();
  });

  it('se ferme sans résultat par Échap ou par un clic à côté', async () => {
    const user = userEvent.setup();
    const { container } = render(<DialogHost />);
    const first = open();
    await user.keyboard('{Escape}');
    await expect(first).resolves.toBeUndefined();
    const second = open();
    await user.pointer({ keys: '[MouseLeft]', target: document.querySelector('.dk-dialog-backdrop') });
    await expect(second).resolves.toBeUndefined();
    expect(container.ownerDocument.querySelector('.dk-dialog')).toBeNull();
  });

  it('reste ouverte quand disableClose est vrai', async () => {
    const user = userEvent.setup();
    render(<DialogHost />);
    open({ disableClose: true });
    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('applique les dimensions demandées', () => {
    render(<DialogHost />);
    open({ width: 'min(480px, 94vw)', panelClass: 'large' });
    const panel = screen.getByRole('dialog');
    expect(panel).toHaveClass('dk-dialog', 'large');
    expect(panel.style.width).toBe('min(480px, 94vw)');
    expect(panel.style.maxWidth).toBe('80vw');
  });

  it('n’adresse Échap qu’à la boîte du dessus', async () => {
    const user = userEvent.setup();
    render(<DialogHost />);
    const below = open();
    act(() => {
      openDialog(() => <DialogTitle>Dessus</DialogTitle>);
    });
    await user.keyboard('{Escape}');
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByRole('dialog')).toHaveTextContent('Bonjour');
    await user.keyboard('{Escape}');
    await expect(below).resolves.toBeUndefined();
  });
});

describe('confirm', () => {
  it('rend vrai sur confirmation, le bouton de confirmation ayant le focus', async () => {
    const user = userEvent.setup();
    render(<DialogHost />);
    let answer;
    act(() => {
      answer = confirm({ title: 'Supprimer ?', message: 'Ligne 1\nLigne 2', confirm: 'Supprimer', danger: true });
    });
    const button = screen.getByRole('button', { name: 'Supprimer' });
    expect(button).toHaveFocus();
    expect(button).toHaveClass('danger-btn');
    await user.click(button);
    await expect(answer).resolves.toBe(true);
  });

  it('rend faux à l’annulation ou à la fermeture', async () => {
    const user = userEvent.setup();
    render(<DialogHost />);
    let answer;
    act(() => {
      answer = confirm({ title: 'Continuer ?', message: '' });
    });
    await user.click(screen.getByRole('button', { name: 'Annuler' }));
    await expect(answer).resolves.toBe(false);
    act(() => {
      answer = confirm({ title: 'Continuer ?', message: '' });
    });
    await user.keyboard('{Escape}');
    await expect(answer).resolves.toBe(false);
  });
});

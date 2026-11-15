import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Menu } from './Menu';
import { MenuButton } from './MenuButton';
import { MenuItem } from './MenuItem';
import { SubMenu } from './SubMenu';

describe('MenuButton', () => {
  function Actions({ onCopy }) {
    return (
      <MenuButton className="control-btn" label="Actions ▾">
        <MenuItem onClick={onCopy}>Copier le chemin</MenuItem>
        <MenuItem disabled>Archiver</MenuItem>
      </MenuButton>
    );
  }

  it('ouvre le menu sous le bouton, entrées désactivées comprises', async () => {
    const user = userEvent.setup();
    render(<Actions />);
    await user.click(screen.getByRole('button', { name: 'Actions ▾' }));
    expect(screen.getByRole('menu')).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Copier le chemin' })).toHaveFocus();
    expect(screen.getByRole('menuitem', { name: 'Archiver' })).toBeDisabled();
  });

  it('exécute l’entrée choisie puis se referme', async () => {
    const user = userEvent.setup();
    const onCopy = vi.fn();
    render(<Actions onCopy={onCopy} />);
    await user.click(screen.getByRole('button', { name: 'Actions ▾' }));
    await user.click(screen.getByRole('menuitem', { name: 'Copier le chemin' }));
    expect(onCopy).toHaveBeenCalledOnce();
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('se referme au second clic sur le bouton, par Échap ou par un clic à côté', async () => {
    const user = userEvent.setup();
    render(
      <>
        <Actions />
        <p>Ailleurs</p>
      </>,
    );
    const button = screen.getByRole('button', { name: 'Actions ▾' });
    await user.click(button);
    await user.click(button);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    await user.click(button);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    await user.click(button);
    await user.click(screen.getByText('Ailleurs'));
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(button).toHaveAttribute('aria-expanded', 'false');
  });
});

describe('Menu contextuel et sous-menu', () => {
  function Tokens({ onInsert }) {
    const [at, setAt] = useState(null);
    return (
      <>
        <button onContextMenu={e => (e.preventDefault(), setAt({ x: e.clientX, y: e.clientY }))}>Ligne</button>
        <Menu at={at} onClose={() => setAt(null)}>
          <SubMenu label="Titre">
            <MenuItem onClick={() => onInsert('%titre%')}>Titre du morceau</MenuItem>
          </SubMenu>
        </Menu>
      </>
    );
  }

  it('s’ouvre au point du clic droit et ouvre un sous-menu', async () => {
    const user = userEvent.setup();
    const onInsert = vi.fn();
    render(<Tokens onInsert={onInsert} />);
    await user.pointer({
      keys: '[MouseRight]',
      target: screen.getByText('Ligne'),
      coords: { clientX: 40, clientY: 60 },
    });
    expect(screen.getByRole('menu')).toHaveStyle({ left: '40px', top: '60px' });
    await user.click(screen.getByRole('menuitem', { name: 'Titre' }));
    expect(screen.getByRole('menuitem', { name: 'Titre' })).toHaveAttribute('aria-expanded', 'true');
    await user.click(screen.getByRole('menuitem', { name: 'Titre du morceau' }));
    expect(onInsert).toHaveBeenCalledWith('%titre%');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });
});

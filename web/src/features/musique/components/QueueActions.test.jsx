import { afterEach, describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithRouter } from '../../../test/render';
import { auth } from '../../../core/auth/auth';
import { QueueActions } from './QueueActions';

const noop = () => undefined;
const props = {
  hasSelection: true,
  canRetry: true,
  canExtract: true,
  onSelectAll: noop,
  onRemoveSelection: noop,
  onPurge: noop,
  onRetry: noop,
  onSearch: noop,
  onExtract: noop,
};

afterEach(() => auth.setState({ session: null }));

describe('QueueActions', () => {
  it('sans le droit de télécharger, désactive les actions sur la file', () => {
    auth.setState({
      session: { authEnabled: true, authenticated: true, role: 'user', pages: ['musique'], canDownload: false },
    });
    renderWithRouter(<QueueActions {...props} />);
    for (const name of ['Extraire', 'Réessayer', 'Purger les téléchargés', 'Retirer la sélection'])
      expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Rechercher' })).toBeEnabled();
  });

  it('avec le droit, laisse les actions ouvertes', () => {
    auth.setState({
      session: { authEnabled: true, authenticated: true, role: 'user', pages: ['musique'], canDownload: true },
    });
    renderWithRouter(<QueueActions {...props} />);
    expect(screen.getByRole('button', { name: 'Extraire' })).toBeEnabled();
  });
});

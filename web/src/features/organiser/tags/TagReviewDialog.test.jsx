import { act, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mockApi } from '../../../test/mock-api';
import { makeJob, renderWithRouter } from '../../../test/render';
import { openTagReviewDialog } from './open-tag-review-dialog';

const job = makeJob({ id: 'job-t', service: 'catalog', type: 'tags.find', status: 'RUNNING' });
const file = (name, fields = {}) => ({
  index: 0,
  path: `D:\\m\\${name}`,
  dbPath: `m/${name}`,
  relative: name,
  title: name,
  titleTagged: false,
  artist: '',
  album: '',
  coreMissing: true,
  ...fields,
});
const DATA = { sessionId: 's1', job, files: [file('a.mp3'), file('b.mp3', { index: 1 })] };
const PROPOSAL = {
  proposed: { title: 'Alpha', artist: 'Inna', album: 'Hot' },
  missing: ['title', 'artist', 'album'],
  contradicted: [],
  score: 0.95,
  confidence: 'sure',
  source: 'Deezer',
  note: null,
};

function server(extra = {}) {
  return mockApi({
    OrganizeProposals: { organizeProposals: [] },
    CancelJob: { cancelJob: { ...job, status: 'CANCELLED', updatedAt: 2 } },
    ...extra,
  });
}

function openReview() {
  let promise;
  act(() => {
    promise = openTagReviewDialog(DATA);
  });
  return promise;
}

describe('openTagReviewDialog', () => {
  it('ne se ferme pas par Échap et remplit les lignes au fil de la recherche', async () => {
    const api = server();
    const { user } = renderWithRouter(<div />);
    openReview();
    expect(await screen.findByText('Recherche en cours… 0 / 2 fichiers')).toBeVisible();

    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog')).toBeVisible();

    const item = {
      jobId: 'job-t',
      type: 'tags.find',
      itemKey: 'm/a.mp3',
      status: 'found',
      message: null,
      data: PROPOSAL,
    };
    act(() => api.emit('JobItem', { jobItem: item }));
    expect(await screen.findByText('Alpha · Inna · Hot')).toBeVisible();
    expect(screen.getByRole('checkbox', { name: 'Écrire a.mp3' })).toBeChecked();
    expect(screen.getByText('Recherche en cours… 1 / 2 fichiers')).toBeVisible();
  });

  it('écrit les correspondances cochées et rend le nombre de fichiers écrits', async () => {
    const written = makeJob({ id: 'job-w', status: 'SUCCEEDED', result: { written: 1, summary: '1 fichier écrit.' } });
    const api = server({
      OrganizeProposals: {
        organizeProposals: [{ jobId: 'job-t', itemKey: 'm/a.mp3', status: 'found', data: PROPOSAL }],
      },
      WriteOrganizeTags: { writeOrganizeTags: written },
    });
    const { user } = renderWithRouter(<div />);
    const result = openReview();

    await user.click(await screen.findByRole('button', { name: 'Écrire les tags cochés' }));
    const question = (await screen.findAllByRole('dialog')).at(-1);
    expect(question).toHaveTextContent('Écrire 3 tag(s) dans 1 fichier(s) ?');
    expect(question).toHaveTextContent("1 fichier(s) n'ont pas été interrogés");
    await user.click(within(question).getByRole('button', { name: 'Oui' }));

    await expect(result).resolves.toBe(1);
    expect(api.mutations('CancelJob')).toEqual([{ service: 'catalog', id: 'job-t' }]);
    expect(api.mutations('WriteOrganizeTags')).toEqual([
      {
        sessionId: 's1',
        items: [
          {
            path: 'm/a.mp3',
            fields: [
              { field: 'title', value: 'Alpha' },
              { field: 'artist', value: 'Inna' },
              { field: 'album', value: 'Hot' },
            ],
            overwriteExisting: false,
          },
        ],
      },
    ]);
    expect(await screen.findByText('1 fichier écrit.')).toBeVisible();
  });

  it('« Annuler » arrête la recherche et rend undefined', async () => {
    const api = server();
    const { user } = renderWithRouter(<div />);
    const result = openReview();
    await user.click(await screen.findByRole('button', { name: 'Annuler' }));
    await expect(result).resolves.toBeUndefined();
    expect(api.mutations('CancelJob')).toHaveLength(1);
  });
});

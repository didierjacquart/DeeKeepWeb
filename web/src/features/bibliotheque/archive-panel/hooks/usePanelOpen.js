import { useState } from 'react';
import { useStore } from '../../../../lib/store';
import { archiveActions } from '../archive-actions';

/** Volet déplié ou non ; une archive demandée sans archive configurée le déplie. */
export function usePanelOpen() {
  const [open, setOpen] = useState(false);
  const openPanelRequest = useStore(archiveActions, s => s.openPanelRequest);
  const [seenRequest, setSeenRequest] = useState(openPanelRequest);
  if (openPanelRequest !== seenRequest) {
    setSeenRequest(openPanelRequest);
    setOpen(true);
  }
  return [open, setOpen];
}

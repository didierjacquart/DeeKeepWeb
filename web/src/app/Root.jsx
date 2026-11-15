import { Outlet } from 'react-router';
import { Toaster } from '../core/ui/Toaster';
import { DialogHost } from '../shared/dialog/DialogHost';
import { TooltipHost } from '../shared/tooltip/TooltipHost';

/** Racine : la page courante et les couches flottantes communes (boîtes, infobulles, messages). */
export function Root() {
  return (
    <>
      <Outlet />
      <DialogHost />
      <TooltipHost />
      <Toaster />
    </>
  );
}

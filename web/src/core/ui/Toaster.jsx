import { createPortal } from 'react-dom';
import { useStore } from '../../lib/store';
import { notify } from './notify';

/** Message éphémère en bas d'écran, au-dessus du lecteur. */
export function Toaster() {
  const toast = useStore(notify, s => s.toast);
  if (!toast) return null;
  return createPortal(
    <div className="dk-toast-wrap">
      <div className={`dk-toast ${toast.kind}`} role={toast.kind === 'error' ? 'alert' : 'status'}>
        <span className="msg">{toast.message}</span>
        <button className="act" onClick={() => notify.dismiss(toast.id)}>
          {toast.action}
        </button>
      </div>
    </div>,
    document.body,
  );
}

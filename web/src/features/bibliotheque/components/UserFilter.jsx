import { useWatch } from '../../../core/api/useWatch';
import { USER_COUNTS } from '../library-api';

/**
 * Administrateur : bibliothèque de tous, ou d'un utilisateur choisi (« les titres de Bob »).
 * @param {{ value: string | null, onChange: (userId: string | null) => void }} props
 */
export function UserFilter({ value, onChange }) {
  const watched = useWatch(USER_COUNTS);
  const users = watched.data?.userCounts ?? [];
  if (!users.length) return null;
  return (
    <label className="lib-user-filter">
      Titres de :{' '}
      <select className="dk-select" value={value ?? ''} onChange={e => onChange(e.target.value || null)}>
        <option value="">tous les utilisateurs</option>
        {users.map(u => (
          <option key={u.id} value={u.id}>
            {u.name} ({u.tracks})
          </option>
        ))}
      </select>
    </label>
  );
}

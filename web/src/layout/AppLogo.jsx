import './app-logo.scss';

/** Logo : carré arrondi cyan portant une croche, « DeeKeep » et sa devise. */
export function AppLogo({ compact = false, className }) {
  return (
    <div className={className ? `dk-app-logo ${className}` : 'dk-app-logo'}>
      <svg className="icon" viewBox="0 0 128 128" aria-hidden="true">
        <rect width="128" height="128" rx="30" fill="#00e5ff" />
        <path d="M52 32 L92 24 V82 a14 14 0 1 1 -8 -12.6 V44 L60 49 V90 a14 14 0 1 1 -8 -12.6 Z" fill="#00363d" />
      </svg>
      {!compact && (
        <div className="text">
          <span className="title">DeeKeep</span>
          <span className="tagline">A U D I O P H I L E &nbsp; G R A D E</span>
        </div>
      )}
    </div>
  );
}

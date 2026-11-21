import { endpoint } from '../../core/api/endpoints';

/** Appels serveur de la page Organiser (session d'analyse, plan, cases, rangement, tags). */

export const STATE = endpoint('OrganizeState', 'organizeState', 'GET /api/organize/state');
export const NAME_TAGS = endpoint('OrganizeNameTags', 'organizeNameTags', 'GET /api/organize/name-tags');
export const ENTRIES = endpoint('OrganizeEntries', 'organizeEntries', 'GET /api/organize/sessions/{sessionId}/entries');
/** Variables : `sessionId`, `options` (absentes = celles des Paramètres). */
export const PLAN = endpoint('OrganizePlan', 'organizePlan', 'POST /api/organize/sessions/{sessionId}/plan');
export const PREVIEW = endpoint('OrganizePreview', 'organizePreview', 'POST /api/organize/preview');
export const CHECKS = endpoint('OrganizeChecks', 'organizeChecks', 'GET /api/organize/sessions/{sessionId}/checks');
export const PROPOSALS = endpoint('OrganizeProposals', 'organizeProposals', 'GET /api/organize/proposals/{jobId}');

export const START = endpoint('StartOrganizeAnalysis', 'startOrganizeAnalysis', 'POST /api/organize/analyze');
export const SAVE_OPTIONS = endpoint('SaveOrganizeOptions', 'saveOrganizeOptions', 'PUT /api/organize/options', {
  body: v => v.options,
});
export const SET_CHECKS = endpoint(
  'SetOrganizeChecks',
  'setOrganizeChecks',
  'POST /api/organize/sessions/{sessionId}/checks',
);
export const RESET_CHECKS = endpoint(
  'ResetOrganizeChecks',
  'resetOrganizeChecks',
  'POST /api/organize/sessions/{sessionId}/checks/reset',
);
export const APPLY = endpoint('ApplyOrganize', 'applyOrganize', 'POST /api/organize/sessions/{sessionId}/apply');
export const DUPLICATES = endpoint(
  'FindOrganizeDuplicates',
  'findOrganizeDuplicates',
  'POST /api/organize/sessions/{sessionId}/duplicates',
);
export const LOOKUP = endpoint(
  'StartOrganizeLookup',
  'startOrganizeLookup',
  'POST /api/organize/sessions/{sessionId}/lookup',
);
export const WRITE_TAGS = endpoint('WriteOrganizeTags', 'writeOrganizeTags', 'POST /api/organize/write-tags');
export const DELETE_FILES = endpoint('DeleteOrganizeFiles', 'deleteOrganizeFiles', 'POST /api/organize/delete-files');

/** Groupes de doublons : résultat de la tâche organize.duplicates. */

import { endpoint } from '../../core/api/endpoints';

/**
 * @typedef {object} AdminUser
 * @property {string} id
 * @property {string} name
 * @property {'admin' | 'user'} role
 * @property {string[]} pages
 * @property {boolean} canDownload
 * @property {boolean} canDelete
 * @property {boolean} disabled
 * @property {number} createdAt
 */

export const USERS = endpoint('AdminUsers', 'users', 'GET /api/admin/users');
export const CREATE_USER = endpoint('CreateUser', 'createUser', 'POST /api/admin/users');
export const UPDATE_USER = endpoint('UpdateUser', 'updateUser', 'PUT /api/admin/users/{id}');
export const RESET_PASSWORD = endpoint('ResetPassword', 'resetPassword', 'POST /api/admin/users/{id}/password');
export const DELETE_USER = endpoint('DeleteUser', 'deleteUser', 'POST /api/admin/users/{id}/delete');

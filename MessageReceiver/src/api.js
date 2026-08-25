// The notification list is server-owned. SignalR only nudges; everything rendered comes from
// here, so there is exactly one shape and one code path producing notifications.

async function request(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    throw new Error(`${options?.method ?? 'GET'} ${url} failed: ${response.status}`);
  }
  return response;
}

export async function fetchNotifications(userId, { unread = false } = {}) {
  const params = new URLSearchParams({ userId });
  if (unread) params.set('unread', 'true');
  const response = await request(`/api/notifications?${params}`);
  return response.json();
}

export async function markRead(userId, id) {
  const params = new URLSearchParams({ userId });
  // userId is the Cosmos partition key, so it is required rather than cosmetic: without it
  // the server cannot do a point write, and cannot confine the caller to their own items.
  await request(`/api/notifications/${encodeURIComponent(id)}/read?${params}`, { method: 'POST' });
}

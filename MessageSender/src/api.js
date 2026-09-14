// The send side of the pipeline. POST enqueues onto Service Bus and returns immediately —
// the response says "accepted", never "delivered". Everything about eventual delivery is a
// UI concern, handled in App.jsx.

async function request(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    // The send endpoint answers 400 with { "error": "..." }, and that text is the whole
    // point on a form. Fall back to the status line when there is no such body — a 502 from
    // the proxy, or a 404 before the endpoint exists.
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.error ?? `${options?.method ?? 'GET'} ${url} failed: ${response.status}`);
  }
  return response;
}

export async function sendNotification(receiverId, content) {
  const response = await request('/api/notifications', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ receiverId, content }),
  });
  // 202 with { id, receiverId } is the contract, but a bodyless 202 is a legal answer to
  // "enqueue this". Returning null rather than throwing keeps the caller honest about what
  // it actually knows.
  return response.json().catch(() => null);
}

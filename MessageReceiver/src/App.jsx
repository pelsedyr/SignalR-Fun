import { useCallback, useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { MdAlertMessage, MdBadge, MdButton, MdIconButton, MdIconCheckCircle, MdIconInfo, MdIconPerson, MdIconSchedule, MdInput } from '@miljodirektoratet/md-react';
import './App.css';
import miljodirektoratetLogo from './assets/miljodirektoratet-logo-white.svg';
import { fetchNotifications, markRead } from './api';

// Collapses a burst of nudges into one fetch. Several notifications arriving together produce
// several nudges but a single request, so the pull path gets cheaper as the rate rises.
const NUDGE_DEBOUNCE_MS = 300;

function App() {
  const [userId, setUserId] = useState('user-123');
  const [status, setStatus] = useState('disconnected');
  const [notifications, setNotifications] = useState([]);
  const [newNotificationIds, setNewNotificationIds] = useState(() => new Set());
  const [loadError, setLoadError] = useState(null);
  const connectionRef = useRef(null);
  const refreshTimerRef = useRef(null);
  const notificationIdsRef = useRef(new Set());
  // Read through a ref inside the SignalR callback: the handler is registered once per
  // connection and would otherwise close over the userId from that render.
  const userIdRef = useRef(userId);
  useEffect(() => { userIdRef.current = userId; }, [userId]);

  const isConnected = status === 'connected';
  const isConnecting = status === 'connecting';
  const hasError = status.startsWith('error:');
  const unreadCount = notifications.filter((n) => !n.readUtc).length;

  // The single path that produces notifications. Every nudge, the initial backfill and every
  // mark-as-read all funnel through here, so the rendered list always comes from Cosmos.
  const refresh = useCallback(async (id, { animateNew = false } = {}) => {
    try {
      const fetchedNotifications = await fetchNotifications(id ?? userIdRef.current);
      const fetchedIds = new Set(fetchedNotifications.map((notification) => notification.id));

      // SignalR only tells us to refresh. Compare its API response to the rendered list so
      // only cards that arrived because of that nudge receive the entrance animation.
      if (animateNew) {
        setNewNotificationIds(new Set(
          fetchedNotifications
            .filter((notification) => !notificationIdsRef.current.has(notification.id))
            .map((notification) => notification.id),
        ));
      }
      notificationIdsRef.current = fetchedIds;
      setNotifications(fetchedNotifications);
      setLoadError(null);
    } catch (err) {
      console.error(err);
      setLoadError(err.message);
    }
  }, []);

  const disconnect = useCallback(async () => {
    clearTimeout(refreshTimerRef.current);
    if (connectionRef.current) {
      await connectionRef.current.stop();
      connectionRef.current = null;
    }
    setStatus('disconnected');
  }, []);

  const connect = useCallback(async () => {
    if (!userId.trim()) return;
    await disconnect();
    setStatus('connecting');
    try {
      const res = await fetch(`/api/negotiate?userId=${encodeURIComponent(userId)}`, { method: 'POST' });
      if (!res.ok) throw new Error(`negotiate failed: ${res.status}`);
      const info = await res.json();
      const connection = new signalR.HubConnectionBuilder().withUrl(info.url, { accessTokenFactory: () => info.accessToken }).withAutomaticReconnect().build();

      // The push is a nudge — { id, createdUtc }, no content. It says "something changed",
      // and the list is re-read from the API. The payload is deliberately ignored: refetching
      // unconditionally is what makes duplicate and out-of-order pushes harmless, and it also
      // picks up changes a nudge never announced (a mark-as-read from another tab).
      connection.on('notificationReceived', () => {
        clearTimeout(refreshTimerRef.current);
        refreshTimerRef.current = setTimeout(() => refresh(undefined, { animateNew: true }), NUDGE_DEBOUNCE_MS);
      });
      connection.onreconnected(() => refresh());
      connection.onclose(() => setStatus('disconnected'));

      await connection.start();
      connectionRef.current = connection;
      setStatus('connected');
      // Backfill: everything that arrived while disconnected is already in Cosmos.
      await refresh(userId);
    } catch (err) {
      console.error(err);
      setStatus(`error: ${err.message}`);
    }
  }, [userId, disconnect, refresh]);

  const onMarkRead = useCallback(async (id) => {
    try {
      await markRead(userIdRef.current, id);
      await refresh();
    } catch (err) {
      console.error(err);
      setLoadError(err.message);
    }
  }, [refresh]);

  useEffect(() => () => { disconnect(); }, [disconnect]);

  const statusTheme = hasError ? 'error' : isConnected ? 'success' : 'info';
  const statusLabel = hasError ? 'Tilkobling feilet' : isConnected ? 'Tilkoblet' : isConnecting ? 'Kobler til' : 'Frakoblet';

  return (
    <div className="app-shell">
      <header className="app-header"><div className="header-content">
        <img className="brand-logo" src={miljodirektoratetLogo} alt="Miljødirektoratet" />
        <div><p className="eyebrow">SignalR · meldingsmottaker</p><h1>Følg varslene mens de skjer</h1></div>
        <div className="notification-indicator" role="status" aria-label={`${unreadCount} uleste varsler`}>
          <span className="notification-bell" aria-hidden="true">🔔</span>
          <span className="notification-badge">{unreadCount}</span>
        </div>
      </div></header>

      <main className="main-content">
        <section className="connection-panel" aria-labelledby="connection-heading">
          <div><p className="section-kicker">Tilkobling</p><h2 id="connection-heading">Velg mottaker</h2></div>
          <div className="connection-controls">
            <MdInput label="Mottaker-ID" value={userId} onChange={(event) => setUserId(event.target.value)} disabled={isConnected || isConnecting} prefixIcon={<MdIconPerson />} supportText="Må samsvare med ReceiverId i meldingen du sender." mode="medium" />
            {isConnected ? <MdButton theme="secondary" onClick={disconnect}>Koble fra</MdButton> : <MdButton theme="primary" onClick={connect} disabled={isConnecting || !userId.trim()}>{isConnecting ? 'Kobler til …' : 'Koble til'}</MdButton>}
          </div>
          <div className="connection-state" aria-live="polite"><MdBadge theme={statusTheme} size="medium">{statusLabel}</MdBadge><span>{hasError ? status.replace('error: ', '') : isConnected ? `Lytter som ${userId}` : 'Ingen aktiv SignalR-tilkobling'}</span></div>
        </section>

        <section className="inbox" aria-labelledby="inbox-heading">
          <div className="inbox-heading"><div><p className="section-kicker">Innboks</p><h2 id="inbox-heading">Mottatte varsler</h2></div><span className="message-count">{unreadCount} uleste · {notifications.length} totalt</span></div>
          {hasError && <MdAlertMessage theme="error" label="Kunne ikke koble til" description={status.replace('error: ', '')} fullWidth />}
          {loadError && <MdAlertMessage theme="error" label="Kunne ikke hente varsler" description={loadError} fullWidth />}
          {notifications.length === 0 ? <div className="empty-state"><MdIconInfo /><h3>Ingen varsler ennå</h3><p>{isConnected ? 'Denne siden oppdateres automatisk når et varsel mottas.' : 'Koble til for å hente varslene dine.'}</p></div> : <ol className="notification-list">{notifications.map((notification) => <li className={`${notification.readUtc ? 'notification-card is-read' : 'notification-card'}${newNotificationIds.has(notification.id) ? ' is-new' : ''}`} key={notification.id}><div className="notification-meta"><span><MdIconSchedule /> {new Date(notification.createdUtc).toLocaleString('nb-NO')}</span>{notification.readUtc ? <span className="read-flag" title="Lest"><MdIconCheckCircle aria-hidden="true" /></span> : <MdIconButton label="Marker som lest" showTooltip theme="plain" className="mark-read-button" onClick={() => onMarkRead(notification.id)}><span aria-hidden="true">👍</span></MdIconButton>}</div><p className="notification-content">{notification.content}</p></li>)}</ol>}
        </section>
      </main>
    </div>
  );
}

export default App;

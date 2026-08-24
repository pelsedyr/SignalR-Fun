import { useCallback, useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { MdAlertMessage, MdBadge, MdButton, MdIconCheckCircle, MdIconInfo, MdIconPerson, MdIconSchedule, MdInput } from '@miljodirektoratet/md-react';
import './App.css';
import miljodirektoratetLogo from './assets/miljodirektoratet-logo-white.svg';

function formatMessage(body) {
  return typeof body === 'string' ? body : JSON.stringify(body, null, 2);
}

function App() {
  const [userId, setUserId] = useState('user-123');
  const [status, setStatus] = useState('disconnected');
  const [notifications, setNotifications] = useState([]);
  const connectionRef = useRef(null);
  const isConnected = status === 'connected';
  const isConnecting = status === 'connecting';
  const hasError = status.startsWith('error:');

  const disconnect = useCallback(async () => {
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
      connection.on('notificationReceived', (body) => {
        let parsed = body;
        try { parsed = JSON.parse(body); } catch { /* retain plain text */ }
        setNotifications((previous) => [{ receivedAt: new Date().toISOString(), body: parsed }, ...previous]);
      });
      connection.onclose(() => setStatus('disconnected'));
      await connection.start();
      connectionRef.current = connection;
      setStatus('connected');
    } catch (err) {
      console.error(err);
      setStatus(`error: ${err.message}`);
    }
  }, [userId, disconnect]);

  useEffect(() => () => { disconnect(); }, [disconnect]);

  const statusTheme = hasError ? 'error' : isConnected ? 'success' : 'info';
  const statusLabel = hasError ? 'Tilkobling feilet' : isConnected ? 'Tilkoblet' : isConnecting ? 'Kobler til' : 'Frakoblet';

  return (
    <div className="app-shell">
      <header className="app-header"><div className="header-content">
        <img className="brand-logo" src={miljodirektoratetLogo} alt="Miljødirektoratet" />
        <div><p className="eyebrow">SignalR · meldingsmottaker</p><h1>Følg varslene mens de skjer</h1></div>
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
          <div className="inbox-heading"><div><p className="section-kicker">Innboks</p><h2 id="inbox-heading">Mottatte varsler</h2></div><span className="message-count">{notifications.length} {notifications.length === 1 ? 'varsel' : 'varsler'}</span></div>
          {hasError && <MdAlertMessage theme="error" label="Kunne ikke koble til" description={status.replace('error: ', '')} fullWidth />}
          {notifications.length === 0 ? <div className="empty-state"><MdIconInfo /><h3>Ingen varsler ennå</h3><p>{isConnected ? 'Denne siden oppdateres automatisk når et varsel mottas.' : 'Koble til for å begynne å lytte etter varsler.'}</p></div> : <ol className="notification-list">{notifications.map((notification, index) => <li className="notification-card" key={`${notification.receivedAt}-${index}`}><div className="notification-meta"><span><MdIconSchedule /> {new Date(notification.receivedAt).toLocaleString('nb-NO')}</span><MdIconCheckCircle aria-label="Mottatt" /></div><pre>{formatMessage(notification.body)}</pre></li>)}</ol>}
        </section>
      </main>
    </div>
  );
}

export default App;

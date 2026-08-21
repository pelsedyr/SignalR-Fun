import { useCallback, useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import './App.css';

// Minimal receiver: enter the same ReceiverId you post with ServiceBusPostTool, connect,
// and pushed notifications appear below as they arrive.
function App() {
  const [userId, setUserId] = useState('user-123');
  const [status, setStatus] = useState('disconnected');
  const [notifications, setNotifications] = useState([]);
  const connectionRef = useRef(null);

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
      const res = await fetch(`/api/negotiate?userId=${encodeURIComponent(userId)}`, {
        method: 'POST',
      });
      if (!res.ok) {
        throw new Error(`negotiate failed: ${res.status}`);
      }
      const info = await res.json();

      const connection = new signalR.HubConnectionBuilder()
        .withUrl(info.url, { accessTokenFactory: () => info.accessToken })
        .withAutomaticReconnect()
        .build();

      connection.on('notificationReceived', (body) => {
        let parsed = body;
        try {
          parsed = JSON.parse(body);
        } catch {
          // leave as raw string if it isn't JSON
        }
        setNotifications((prev) => [{ receivedAt: new Date().toISOString(), body: parsed }, ...prev]);
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

  return (
    <div className="App">
      <header className="App-header">
        <h1>SignalR-Fun receiver</h1>

        <p>
          Mottaker ID{' '}
          <br/>
          <input
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
            disabled={status === 'connected' || status === 'connecting'}
          />
        </p>

        {status === 'connected' ? (
          <button onClick={disconnect}>Disconnect</button>
        ) : (
          <button onClick={connect} disabled={status === 'connecting'}>Connect</button>
        )}

        <p>Status: {status}</p>

        <ul style={{ textAlign: 'left' }}>
          {notifications.map((n, i) => (
            <li key={i}>
              <code>{n.receivedAt}</code> — {typeof n.body === 'string' ? n.body : JSON.stringify(n.body)}
            </li>
          ))}
        </ul>
      </header>
    </div>
  );
}

export default App;

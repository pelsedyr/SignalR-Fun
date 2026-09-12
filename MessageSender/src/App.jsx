import { useCallback, useRef, useState } from 'react';
import { MdAlertMessage, MdButton, MdIconArrowForward, MdIconInfo, MdIconPerson, MdIconSchedule, MdInput, MdTextArea } from '@miljodirektoratet/md-react';
import './App.css';
import miljodirektoratetLogo from './assets/miljodirektoratet-logo-white.svg';
import { sendNotification } from './api';

// Mirrors Strings.Notifications.MaxContentLength in MessageHub. Enforced here with
// maxLength so the server's 400 is unreachable from this UI rather than merely unlikely --
// the server stays the authority either way.
const MAX_CONTENT_LENGTH = 4096;
// Keeps the session log from growing without bound in a long demo.
const MAX_LOG_ENTRIES = 20;

function App() {
  const [receiverId, setReceiverId] = useState('bursdag');
  const [content, setContent] = useState('');
  const [status, setStatus] = useState('idle');
  const [errorMessage, setErrorMessage] = useState(null);
  const [sentLog, setSentLog] = useState([]);
  // Nothing turns red until the first submit attempt.
  const [submitted, setSubmitted] = useState(false);
  const contentRef = useRef(null);

  const isSending = status === 'sending';
  const trimmedReceiverId = receiverId.trim();
  const trimmedContent = content.trim();
  const receiverError = submitted && !trimmedReceiverId;
  const contentError = submitted && !trimmedContent;

  const onSubmit = useCallback(async (event) => {
    event.preventDefault();
    setSubmitted(true);

    const nextReceiverId = receiverId.trim();
    const nextContent = content.trim();
    if (isSending || !nextReceiverId || !nextContent) return;

    setStatus('sending');
    setErrorMessage(null);

    try {
      const accepted = await sendNotification(nextReceiverId, nextContent);

      // The id and timestamp are the server's when it supplies them. The fallbacks exist so
      // a bodyless 202 still produces a usable log entry -- see api.js.
      setSentLog((entries) => [
        {
          id: accepted?.id ?? crypto.randomUUID(),
          receiverId: accepted?.receiverId ?? nextReceiverId,
          content: nextContent,
          queuedAt: new Date().toISOString(),
        },
        ...entries,
      ].slice(0, MAX_LOG_ENTRIES));

      // Clear the message but keep the recipient: sending several messages to the same
      // receiver is the common flow, and retyping the id every time is pure friction.
      setContent('');
      setSubmitted(false);
      setStatus('sent');
      contentRef.current?.focus();
    } catch (err) {
      console.error(err);
      setErrorMessage(err.message);
      setStatus('error');
    }
  }, [receiverId, content, isSending]);

  const lastSent = sentLog[0];

  return (
    <div className="app-shell">
      <header className="app-header"><div className="header-content">
        <img className="brand-logo" src={miljodirektoratetLogo} alt="Miljødirektoratet" />
        <div><p className="eyebrow">SignalR · meldingssender</p><h1>Legg en melding på køen</h1></div>
      </div></header>

      <main className="main-content">
        <section className="compose-panel" aria-labelledby="compose-heading">
          <div><p className="section-kicker">Ny melding</p><h2 id="compose-heading">Skriv og send</h2></div>

          <form className="compose-form" onSubmit={onSubmit} noValidate>
            <MdInput
              label="Mottaker-ID"
              value={receiverId}
              onChange={(event) => setReceiverId(event.target.value)}
              disabled={isSending}
              autoComplete="off"
              prefixIcon={<MdIconPerson />}
              supportText="Må samsvare med ID-en mottakeren er koblet til."
              error={receiverError}
              errorText="Mottaker-ID er påkrevd."
              mode="medium"
            />
            <MdTextArea
              ref={contentRef}
              label="Melding"
              rows={5}
              value={content}
              onChange={(event) => setContent(event.target.value)}
              onKeyDown={(event) => {
                // Plain Enter must still make a newline in a message body, so the submit
                // shortcut is the conventional modifier+Enter. requestSubmit, not submit:
                // submit() bypasses the React onSubmit handler entirely.
                if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') event.currentTarget.form?.requestSubmit();
              }}
              disabled={isSending}
              maxLength={MAX_CONTENT_LENGTH}
              placeholder="Hva vil du si?"
              error={contentError}
              errorText="Skriv en melding før du sender."
            />
            <div className="compose-actions">
              <span className="compose-hint">Ctrl + Enter sender</span>
              <span className="compose-counter" aria-hidden="true">{content.length} / {MAX_CONTENT_LENGTH}</span>
              {/* type="submit" is required: MdButton defaults to type="button", and without
                  this neither the click nor Enter would ever reach onSubmit. loading shows a
                  spinner but does not disable, so both props are passed. */}
              <MdButton type="submit" theme="primary" loading={isSending} disabled={isSending} rightIcon={<MdIconArrowForward />}>
                {isSending ? 'Sender …' : 'Send melding'}
              </MdButton>
            </div>
          </form>

          <div className="send-result" aria-live="polite">
            {status === 'sent' && lastSent && (
              <MdAlertMessage
                theme="success"
                label="Lagt på køen"
                description={`Meldingen er lagt på Service Bus-køen og dukker opp hos «${lastSent.receiverId}» i løpet av et øyeblikk.`}
                fullWidth
                closable
                onClose={() => setStatus('idle')}
              />
            )}
            {status === 'error' && (
              <MdAlertMessage theme="error" label="Kunne ikke sende meldingen" description={errorMessage} fullWidth />
            )}
          </div>

          <p className="pipeline-note">
            <MdIconInfo aria-hidden="true" />
            <span>Meldingen går via Service Bus-køen til MessageHub, som lagrer den og varsler mottakeren over SignalR. <strong>Mottakeren trenger ikke være tilkoblet</strong> — meldingen lagres uansett og dukker opp neste gang de kobler til.</span>
          </p>
        </section>

        <section className="sent-log" aria-labelledby="sent-log-heading">
          <div className="sent-log-heading">
            <div><p className="section-kicker">Denne økten</p><h2 id="sent-log-heading">Sendte meldinger</h2></div>
            <span className="message-count">{sentLog.length} sendt i denne økten</span>
          </div>
          {sentLog.length === 0 ? (
            <div className="empty-state">
              <MdIconInfo />
              <h3>Ingen meldinger sendt ennå</h3>
              <p>Skriv en melding over og send den. Listen her viser bare det du har sendt i denne fanen — den hentes ikke fra serveren.</p>
            </div>
          ) : (
            <ol className="sent-list">
              {sentLog.map((entry, index) => (
                <li className={index === 0 ? 'sent-card is-new' : 'sent-card'} key={entry.id}>
                  <div className="sent-meta">
                    <span><MdIconSchedule /> Lagt på kø {new Date(entry.queuedAt).toLocaleString('nb-NO')}</span>
                    <span><MdIconPerson /> {entry.receiverId}</span>
                  </div>
                  <p className="sent-content">{entry.content}</p>
                </li>
              ))}
            </ol>
          )}
        </section>
      </main>
    </div>
  );
}

export default App;

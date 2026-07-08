package main

// Node-to-node chat, the Go side of PlaceContext.Sync.Transport (docs/SYNC-PROTOCOL.md §10).
// Interoperates with the C# implementation byte-for-byte:
//   identity  — ECDSA P-256 key (PKCS#8 PEM on disk), NodeId = crockford-base32(SHA-256(SPKI)[0:20])
//   transport — mutual TLS 1.3; trust = the peer's presented key hashes to the pinned NodeId
//   wire      — uvarint(len) u8(kind) body;  Chat(5) = string text · uvarint sentAtUnixMs;  Bye(4)
// A line's sender is always the TLS-authenticated peer — chat frames carry no name to spoof.

import (
	"bufio"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/binary"
	"encoding/pem"
	"errors"
	"fmt"
	"io"
	"math/big"
	"net"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

const (
	frameBye  = 4
	frameChat = 5

	chatDefaultPort   = 7443
	chatDiscoveryPort = 7444 // UDP: "who is running PlaceContext here?"
	maxFrame          = 1 << 20 // 1 MiB — far above any chat line; bounds hostile lengths

	discoverProbe  = "PCSP-DISCOVER v1"
	discoverPrefix = "PCSP-HERE " // + "<nodeid> <chatPort>"
)

// ── identity ────────────────────────────────────────────────────────────────────────────────────

const b32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ" // Crockford (no I L O U)

// nodeIDFromSPKI mirrors NodeId.FromPublicKey: crockford-base32 of SHA-256(SPKI)[0:20] — 32 chars.
func nodeIDFromSPKI(spki []byte) string {
	h := sha256.Sum256(spki)
	data := h[:20]
	out := make([]byte, 0, 32)
	bitBuf, bits := 0, 0
	for _, b := range data {
		bitBuf = bitBuf<<8 | int(b)
		bits += 8
		for bits >= 5 {
			bits -= 5
			out = append(out, b32Alphabet[(bitBuf>>bits)&0x1F])
		}
	}
	if bits > 0 {
		out = append(out, b32Alphabet[(bitBuf<<(5-bits))&0x1F])
	}
	return string(out)
}

func nodeIDFromCert(c *x509.Certificate) string { return nodeIDFromSPKI(c.RawSubjectPublicKeyInfo) }

type chatIdentity struct {
	nodeID string
	cert   tls.Certificate
}

// loadOrCreateChatIdentity keeps the node's long-lived key at ~/.config/placecontext/node.key
// (PKCS#8 PEM — the same format the C# host persists), so the NodeId is stable across restarts.
func loadOrCreateChatIdentity() (*chatIdentity, error) {
	dir, err := os.UserHomeDir()
	if err != nil {
		return nil, err
	}
	keyPath := filepath.Join(dir, ".config", "placecontext", "node.key")

	var key *ecdsa.PrivateKey
	if pemBytes, err := os.ReadFile(keyPath); err == nil {
		block, _ := pem.Decode(pemBytes)
		if block == nil {
			return nil, fmt.Errorf("%s: not PEM", keyPath)
		}
		parsed, err := x509.ParsePKCS8PrivateKey(block.Bytes)
		if err != nil {
			return nil, fmt.Errorf("%s: %w", keyPath, err)
		}
		ec, ok := parsed.(*ecdsa.PrivateKey)
		if !ok {
			return nil, fmt.Errorf("%s: not an ECDSA key", keyPath)
		}
		key = ec
	} else {
		key, err = ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
		if err != nil {
			return nil, err
		}
		der, err := x509.MarshalPKCS8PrivateKey(key)
		if err != nil {
			return nil, err
		}
		if err := os.MkdirAll(filepath.Dir(keyPath), 0o700); err != nil {
			return nil, err
		}
		pemBytes := pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: der})
		if err := os.WriteFile(keyPath, pemBytes, 0o600); err != nil {
			return nil, err
		}
	}

	spki, err := x509.MarshalPKIXPublicKey(&key.PublicKey)
	if err != nil {
		return nil, err
	}
	nodeID := nodeIDFromSPKI(spki)

	// The certificate is ephemeral (regenerated each start); identity is the key, never the cert.
	serial, _ := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 120))
	now := time.Now()
	tmpl := &x509.Certificate{
		SerialNumber: serial,
		Subject:      pkix.Name{CommonName: nodeID},
		DNSNames:     []string{nodeID},
		NotBefore:    now.Add(-5 * time.Minute),
		NotAfter:     now.AddDate(20, 0, 0),
		KeyUsage:     x509.KeyUsageDigitalSignature,
		ExtKeyUsage:  []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth, x509.ExtKeyUsageClientAuth},
	}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	if err != nil {
		return nil, err
	}
	return &chatIdentity{
		nodeID: nodeID,
		cert:   tls.Certificate{Certificate: [][]byte{der}, PrivateKey: key},
	}, nil
}

// ── wire ────────────────────────────────────────────────────────────────────────────────────────

func appendUvarint(dst []byte, v uint64) []byte { return binary.AppendUvarint(dst, v) }

func encodeChatFrame(text string, sentAtUnixMs int64) []byte {
	body := []byte{frameChat}
	body = appendUvarint(body, uint64(len(text)))
	body = append(body, text...)
	body = appendUvarint(body, uint64(sentAtUnixMs))
	return append(appendUvarint(nil, uint64(len(body))), body...)
}

func encodeByeFrame(reason string) []byte {
	body := []byte{frameBye}
	body = appendUvarint(body, uint64(len(reason)))
	body = append(body, reason...)
	return append(appendUvarint(nil, uint64(len(body))), body...)
}

// readFrame reads one uvarint-length-prefixed frame; returns the kind byte and the body.
func readFrame(r *bufio.Reader) (byte, []byte, error) {
	n, err := binary.ReadUvarint(r)
	if err != nil {
		return 0, nil, err
	}
	if n == 0 || n > maxFrame {
		return 0, nil, fmt.Errorf("bad frame length %d", n)
	}
	payload := make([]byte, n)
	if _, err := io.ReadFull(r, payload); err != nil {
		return 0, nil, err
	}
	return payload[0], payload[1:], nil
}

func decodeChatBody(body []byte) (text string, sentAtUnixMs int64, err error) {
	strLen, n := binary.Uvarint(body)
	if n <= 0 || uint64(len(body)-n) < strLen {
		return "", 0, errors.New("truncated chat text")
	}
	text = string(body[n : n+int(strLen)])
	ms, k := binary.Uvarint(body[n+int(strLen):])
	if k <= 0 {
		return "", 0, errors.New("truncated chat timestamp")
	}
	return text, int64(ms), nil
}

// ── channel ─────────────────────────────────────────────────────────────────────────────────────

type chatLine struct {
	from string // TLS-authenticated peer NodeId ("" = local echo)
	text string
	at   time.Time
}

// chatPeer is a node found by the LAN scan: its self-declared identity (verified by TLS pinning
// the moment we dial it — a liar's handshake fails) and where its chat listener lives.
type chatPeer struct {
	nodeID   string
	hostPort string
}

type chatEvent struct {
	kind    string // "connected" | "request" | "line" | "closed" | "error" | "peers"
	channel *chatChannel
	line    chatLine
	info    string
	peers   []chatPeer
}

type chatChannel struct {
	conn    *tls.Conn
	peerID  string
	writeMu sync.Mutex
	closed  sync.Once
}

func (c *chatChannel) send(text string) error {
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	_, err := c.conn.Write(encodeChatFrame(text, time.Now().UnixMilli()))
	return err
}

// close says Bye (best-effort) and hangs up. Safe to call more than once.
func (c *chatChannel) close(reason string) {
	c.closed.Do(func() {
		c.writeMu.Lock()
		_ = c.conn.SetWriteDeadline(time.Now().Add(2 * time.Second))
		_, _ = c.conn.Write(encodeByeFrame(reason))
		c.writeMu.Unlock()
		_ = c.conn.Close()
	})
}

// readLoop surfaces incoming lines until Bye/EOF, then reports the channel closed.
func (c *chatChannel) readLoop(events chan<- chatEvent) {
	r := bufio.NewReader(c.conn)
	for {
		kind, body, err := readFrame(r)
		if err != nil {
			events <- chatEvent{kind: "closed", channel: c, info: "peer disconnected"}
			return
		}
		switch kind {
		case frameChat:
			text, ms, err := decodeChatBody(body)
			if err != nil {
				c.close("bad frame")
				events <- chatEvent{kind: "closed", channel: c, info: "protocol error"}
				return
			}
			events <- chatEvent{kind: "line", channel: c,
				line: chatLine{from: c.peerID, text: text, at: time.UnixMilli(ms)}}
		case frameBye:
			events <- chatEvent{kind: "closed", channel: c, info: "peer said bye"}
			_ = c.conn.Close()
			return
		default:
			// unknown kinds are ignored (forward compat, §7)
		}
	}
}

// ── TLS posture (identical to TlsPeerAuthentication.cs) ─────────────────────────────────────────

func chatServerTLS(id *chatIdentity) *tls.Config {
	return &tls.Config{
		Certificates: []tls.Certificate{id.cert},
		MinVersion:   tls.VersionTLS13,
		ClientAuth:   tls.RequireAnyClientCert, // must present one; which node it is = the hash check
	}
}

func chatClientTLS(id *chatIdentity, expectedPeer string) *tls.Config {
	return &tls.Config{
		Certificates:       []tls.Certificate{id.cert},
		MinVersion:         tls.VersionTLS13,
		ServerName:         expectedPeer,
		InsecureSkipVerify: true, // chain trust replaced by identity pinning below
		VerifyPeerCertificate: func(rawCerts [][]byte, _ [][]*x509.Certificate) error {
			if len(rawCerts) == 0 {
				return errors.New("peer presented no certificate")
			}
			leaf, err := x509.ParseCertificate(rawCerts[0])
			if err != nil {
				return err
			}
			if got := nodeIDFromCert(leaf); got != expectedPeer {
				return fmt.Errorf("identity mismatch: certificate is %s, expected %s", got, expectedPeer)
			}
			return nil
		},
	}
}

// ── listener + dialer ───────────────────────────────────────────────────────────────────────────

// startChatListener accepts encrypted chat connections and reports each as a "request" —
// nothing is read from the peer until the operator accepts (see chatChannel.start), so a
// conversation cannot begin without permission. busy() short-circuits a second caller politely.
func startChatListener(id *chatIdentity, port int, events chan<- chatEvent, busy func() bool) (net.Listener, error) {
	ln, err := tls.Listen("tcp", fmt.Sprintf(":%d", port), chatServerTLS(id))
	if err != nil {
		return nil, err
	}
	go func() {
		for {
			conn, err := ln.Accept()
			if err != nil {
				return // listener closed
			}
			go func(conn net.Conn) {
				tc := conn.(*tls.Conn)
				_ = tc.SetDeadline(time.Now().Add(10 * time.Second))
				if err := tc.Handshake(); err != nil {
					_ = tc.Close()
					return
				}
				_ = tc.SetDeadline(time.Time{})
				peer := nodeIDFromCert(tc.ConnectionState().PeerCertificates[0])
				ch := &chatChannel{conn: tc, peerID: peer}
				if busy() {
					ch.close("busy: another chat is open")
					return
				}
				events <- chatEvent{kind: "request", channel: ch, info: "incoming"}
			}(conn)
		}
	}()
	return ln, nil
}

// start begins reading from an accepted channel — called when the operator says yes.
func (c *chatChannel) start(events chan<- chatEvent) { go c.readLoop(events) }

// ── discovery (LAN scan) ────────────────────────────────────────────────────────────────────────

// startChatResponder answers discovery probes with this node's identity + chat port, so other
// operators' scans can find it. A bind failure is non-fatal (another local process answers).
func startChatResponder(id *chatIdentity, chatPort int) {
	go func() {
		conn, err := net.ListenUDP("udp4", &net.UDPAddr{Port: chatDiscoveryPort})
		if err != nil {
			return
		}
		defer conn.Close()
		reply := []byte(fmt.Sprintf("%s%s %d", discoverPrefix, id.nodeID, chatPort))
		buf := make([]byte, 64)
		for {
			n, addr, err := conn.ReadFromUDP(buf)
			if err != nil {
				return
			}
			if string(buf[:n]) == discoverProbe {
				_, _ = conn.WriteToUDP(reply, addr)
			}
		}
	}()
}

// scanForPeers broadcasts a probe and collects answers for ~1.5s, then emits one "peers" event.
// Discovery is plaintext by design: it only advertises (nodeid, port); trust is established by
// the TLS handshake pinning that nodeid when the operator chooses to dial.
func scanForPeers(selfID string, events chan<- chatEvent) {
	go func() {
		conn, err := net.ListenUDP("udp4", &net.UDPAddr{})
		if err != nil {
			events <- chatEvent{kind: "error", info: "scan: " + err.Error()}
			return
		}
		defer conn.Close()

		probe := []byte(discoverProbe)
		_, _ = conn.WriteToUDP(probe, &net.UDPAddr{IP: net.IPv4bcast, Port: chatDiscoveryPort})
		_, _ = conn.WriteToUDP(probe, &net.UDPAddr{IP: net.IPv4(127, 255, 255, 255), Port: chatDiscoveryPort})
		// Directed broadcast on each interface's subnet too — plain 255.255.255.255 is
		// dropped by some networks.
		if ifaces, err := net.Interfaces(); err == nil {
			for _, ifc := range ifaces {
				addrs, _ := ifc.Addrs()
				for _, a := range addrs {
					ipn, ok := a.(*net.IPNet)
					if !ok || ipn.IP.To4() == nil {
						continue
					}
					bcast := make(net.IP, 4)
					ip4, mask := ipn.IP.To4(), net.IP(ipn.Mask).To4()
					for i := 0; i < 4; i++ {
						bcast[i] = ip4[i] | ^mask[i]
					}
					_, _ = conn.WriteToUDP(probe, &net.UDPAddr{IP: bcast, Port: chatDiscoveryPort})
				}
			}
		}

		_ = conn.SetReadDeadline(time.Now().Add(1500 * time.Millisecond))
		seen := map[string]bool{}
		var peers []chatPeer
		buf := make([]byte, 128)
		for {
			n, addr, err := conn.ReadFromUDP(buf)
			if err != nil {
				break // deadline: scan window over
			}
			msg := string(buf[:n])
			if !strings.HasPrefix(msg, discoverPrefix) {
				continue
			}
			fields := strings.Fields(strings.TrimPrefix(msg, discoverPrefix))
			if len(fields) != 2 || len(fields[0]) != 32 || fields[0] == selfID || seen[fields[0]] {
				continue
			}
			seen[fields[0]] = true
			peers = append(peers, chatPeer{nodeID: fields[0], hostPort: net.JoinHostPort(addr.IP.String(), fields[1])})
		}
		events <- chatEvent{kind: "peers", peers: peers}
	}()
}

// dialChat connects to host:port and pins expectedPeer — the handshake fails unless the
// certificate presented hashes to exactly that NodeId.
func dialChat(id *chatIdentity, hostPort, expectedPeer string, events chan<- chatEvent) {
	go func() {
		d := &net.Dialer{Timeout: 10 * time.Second}
		conn, err := tls.DialWithDialer(d, "tcp", hostPort, chatClientTLS(id, expectedPeer))
		if err != nil {
			events <- chatEvent{kind: "error", info: err.Error()}
			return
		}
		ch := &chatChannel{conn: conn, peerID: expectedPeer}
		events <- chatEvent{kind: "connected", channel: ch, info: "dialed " + hostPort}
		ch.readLoop(events)
	}()
}

// ── view ────────────────────────────────────────────────────────────────────────────────────────

func (m model) chatView() string {
	var b strings.Builder
	you := "identity unavailable"
	if m.chatID != nil {
		you = m.chatID.nodeID
	}
	b.WriteString(titleStyle.Render(" chat ") +
		dimStyle.Render(fmt.Sprintf("  encrypted node-to-node (mutual TLS 1.3) · you are %s · listening :%d", you, m.chatPort)) + "\n")
	if m.chatErr != "" {
		b.WriteString("  " + errStyle.Render("✗ "+m.chatErr) + "\n")
	}
	b.WriteString("  " + dimStyle.Render("status: ") + m.chatStatus + "\n\n")

	// Incoming request: a modal question the operator must answer.
	if m.chatPending != nil {
		b.WriteString("  " + warnStyle.Render("● "+m.chatPending.peerID) + "\n")
		b.WriteString("  " + warnStyle.Render("wants to chat — ") +
			keyStyle.Render("[y]") + " accept   " + keyStyle.Render("[n]") + " decline\n\n")
	}

	// Discovered nodes (LAN scan) — pick one with tab, dial with ⏎ on an empty input.
	if m.chatActive == nil && m.chatPending == nil && len(m.chatPeers) > 0 {
		b.WriteString("  " + dimStyle.Render("nodes on your network:") + "\n")
		for i, p := range m.chatPeers {
			row := fmt.Sprintf("%s  %s", p.nodeID, p.hostPort)
			if i == m.chatSel {
				b.WriteString("  " + okStyle.Render("❯ "+row) + "\n")
			} else {
				b.WriteString("    " + dimStyle.Render(row) + "\n")
			}
		}
		b.WriteString("\n")
	}

	// Transcript — the last lines that fit the pane.
	paneH := maxInt(6, m.h-22-len(m.chatPeers))
	var t strings.Builder
	if len(m.chatLog) == 0 {
		if m.chatActive == nil {
			t.WriteString(dimStyle.Render("no conversation — ⏎ dials the selected node (each side approves first),\n"))
			t.WriteString(dimStyle.Render("or type  host[:port] NODEID  to dial manually · ctrl+r rescans."))
		} else {
			t.WriteString(dimStyle.Render("connected — type a message and press ⏎"))
		}
	}
	start := maxInt(0, len(m.chatLog)-paneH)
	for _, ln := range m.chatLog[start:] {
		stamp := dimStyle.Render(ln.at.Format("15:04"))
		switch {
		case strings.HasPrefix(ln.text, "— "):
			t.WriteString(stamp + " " + dimStyle.Render(ln.text) + "\n")
		case ln.from == "":
			t.WriteString(stamp + " " + okStyle.Render("you") + "  " + ln.text + "\n")
		default:
			t.WriteString(stamp + " " + keyStyle.Render(ln.from[:8]+"…") + "  " + ln.text + "\n")
		}
	}
	b.WriteString(boxStyle.Render(t.String()) + "\n")

	prompt := "message"
	if m.chatActive == nil {
		prompt = "host[:port] NODEID"
	}
	b.WriteString("  " + keyStyle.Render("> ") + m.chatInput + dimStyle.Render("▌  ("+prompt+")") + "\n")
	return b.String()
}

func maxInt(a, b int) int {
	if a > b {
		return a
	}
	return b
}

// parseChatTarget accepts "host:port NODEID" (port defaults to chatDefaultPort when absent).
func parseChatTarget(s string) (hostPort, nodeID string, err error) {
	fields := strings.Fields(strings.TrimSpace(s))
	if len(fields) != 2 {
		return "", "", errors.New("format: host[:port] NODEID")
	}
	hostPort, nodeID = fields[0], strings.ToUpper(fields[1])
	if !strings.Contains(hostPort, ":") {
		hostPort = fmt.Sprintf("%s:%d", hostPort, chatDefaultPort)
	}
	if len(nodeID) != 32 {
		return "", "", errors.New("NODEID must be 32 characters (shown on the peer's chat screen)")
	}
	return hostPort, nodeID, nil
}

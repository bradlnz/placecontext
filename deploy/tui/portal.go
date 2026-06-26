package main

import (
	"context"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"fmt"
	neturl "net/url"
	"os"
	"os/exec"
	"strings"
	"time"

	tea "github.com/charmbracelet/bubbletea"
)

func portalURL() string {
	port := os.Getenv("PCTL_PORT")
	if port == "" {
		port = "7700"
	}
	return "http://localhost:" + port + "/"
}

// openPortal opens the portal in the default browser (best-effort, cross-platform). The portal has no
// password login: we read the shared signing key from the cluster secret, mint a short-lived token, and
// hand it to /auth/portal so the operator is signed in automatically. With no key reachable (e.g. local
// `./run.sh` with no cluster) we open the bare URL — the host auto-signs-in in Development.
func (m model) openPortal() tea.Cmd {
	target := portalURL()
	if key := m.portalSigningKey(); key != "" {
		target += "auth/portal?token=" + neturl.QueryEscape(mintPortalToken(key, time.Now()))
	}
	return func() tea.Msg {
		var bin string
		var args []string
		switch {
		case commandExists("xdg-open"):
			bin, args = "xdg-open", []string{target}
		case commandExists("open"):
			bin, args = "open", []string{target}
		default:
			return flashMsg("portal: " + portalURL() + " (no browser opener found)")
		}
		cmd := exec.Command(bin, args...)
		cmd.Env = childEnv()
		if err := cmd.Start(); err != nil {
			return flashMsg("portal: " + portalURL() + " (open failed: " + err.Error() + ")")
		}
		return flashMsg("opened " + portalURL() + " in your browser")
	}
}

// billingURL is the separate subscription/billing web portal the operator pays through.
func billingURL() string {
	if u := os.Getenv("PCTL_BILLING_URL"); u != "" {
		return u
	}
	return "https://placecontext.ai/subscribe"
}

// openBilling sends the operator to the billing portal to manage/pay for their subscription.
func (m model) openBilling() tea.Cmd {
	target := billingURL()
	return func() tea.Msg {
		var bin string
		switch {
		case commandExists("xdg-open"):
			bin = "xdg-open"
		case commandExists("open"):
			bin = "open"
		default:
			return flashMsg("subscribe at: " + target + " (no browser opener found)")
		}
		cmd := exec.Command(bin, target)
		cmd.Env = childEnv()
		if err := cmd.Start(); err != nil {
			return flashMsg("subscribe at: " + target + " (open failed: " + err.Error() + ")")
		}
		return flashMsg("opened the subscription portal — complete payment in your browser")
	}
}

// portalSigningKey reads the shared HMAC key from the placecontext-portal cluster secret. Returns "" if
// it can't be read (secret absent, or no cluster), so the caller can fall back to a token-less open.
func (m model) portalSigningKey() string {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	out, err := m.kubectl(ctx, "-n", ns, "get", "secret", "placecontext-portal",
		"-o", "jsonpath={.data.signing-key}")
	if err != nil || len(out) == 0 {
		return ""
	}
	raw, err := base64.StdEncoding.DecodeString(strings.TrimSpace(string(out)))
	if err != nil {
		return ""
	}
	return string(raw)
}

// mintPortalToken builds the 60-second HMAC token the host validates (see Host/Auth/PortalToken.cs):
//
//	payload = "<expUnix>.<nonceHex>"
//	token   = payload + "." + base64url-unpadded( HMAC_SHA256(key, payload) )
func mintPortalToken(key string, now time.Time) string {
	nonce := make([]byte, 8)
	_, _ = rand.Read(nonce)
	payload := fmt.Sprintf("%d.%s", now.Add(60*time.Second).Unix(), hex.EncodeToString(nonce))
	mac := hmac.New(sha256.New, []byte(key))
	mac.Write([]byte(payload))
	return payload + "." + base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}

func commandExists(name string) bool { _, err := exec.LookPath(name); return err == nil }

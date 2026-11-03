use crate::bridge::Result;
use serde_json::{json, Value};
use std::{
    io::{BufRead, BufReader, Write},
    path::{Path, PathBuf},
    process::{Child, ChildStdin, Command, Stdio},
    sync::mpsc::{sync_channel, Receiver},
    thread,
    time::Duration,
};

pub struct Browser {
    child: Child,
    input: ChildStdin,
    replies: Receiver<Value>,
    sequence: u32,
    session: Option<String>,
    target: Value,
    directory: PathBuf,
    pointer: (f64, f64),
}

// CDP's pipe protocol uses NUL-delimited JSON. Bound each frame before parsing it.
fn read_frame(reader: &mut impl BufRead) -> Result<Value> {
    let mut bytes = Vec::new();
    loop {
        let buffer = reader.fill_buf()?;
        if buffer.is_empty() {
            return Err("Chromium disconnected".into());
        }
        let end = buffer.iter().position(|byte| *byte == 0);
        let count = end.unwrap_or(buffer.len());
        if bytes.len() + count > 6_000_000 {
            return Err("Browser response too large".into());
        }
        bytes.extend_from_slice(&buffer[..count]);
        reader.consume(count + usize::from(end.is_some()));
        if end.is_some() {
            return Ok(serde_json::from_slice(&bytes)?);
        }
    }
}

impl Browser {
    pub fn launch(directory: &Path) -> Result<Self> {
        std::fs::create_dir_all("/home/workflow/.local/share/placecontext-browser")?;
        // Shell only maps inherited pipe descriptors; task data is never shell text.
        let mut child = Command::new("/bin/sh").args(["-c",
            "exec chromium --no-first-run --no-default-browser-check --disable-background-networking --start-maximized --remote-debugging-pipe --user-data-dir=\"$1\" about:blank 3<&0 4>&1 </dev/null >/dev/null",
            "pc-jev"]).arg(Path::new("/home/workflow/.local/share/placecontext-browser"))
            .stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null()).spawn()?;
        let input = child.stdin.take().ok_or("Missing browser input")?;
        let output = child.stdout.take().ok_or("Missing browser output")?;
        let (sender, replies) = sync_channel(1);
        thread::spawn(move || {
            let mut reader = BufReader::new(output);
            while let Ok(message) = read_frame(&mut reader) {
                if message.get("id").is_some() && sender.send(message).is_err() {
                    break;
                }
            }
        });
        Ok(Self {
            child,
            input,
            replies,
            sequence: 0,
            session: None,
            target: Value::Null,
            directory: directory.into(),
            pointer: (640.0, 400.0),
        })
    }

    fn command(&mut self, method: &str, params: Value) -> Result<Value> {
        self.sequence += 1;
        let mut message = json!({"id": self.sequence, "method": method, "params": params});
        if let Some(session) = &self.session {
            message["sessionId"] = json!(session);
        }
        self.input.write_all(&serde_json::to_vec(&message)?)?;
        self.input.write_all(&[0])?;
        self.input.flush()?;
        // Cold Chromium startup under software emulation is much slower than later commands.
        let reply = self
            .replies
            .recv_timeout(Duration::from_secs(match self.sequence {
                1 => 60,
                _ => 15,
            }))
            .map_err(|error| format!("Browser did not respond to {method}: {error}"))?;
        if reply["id"].as_u64() != Some(self.sequence.into()) || reply.get("error").is_some() {
            return Err(format!("Browser command failed: {method}").into());
        }
        Ok(reply["result"].clone())
    }

    pub fn open(&mut self, url: &str) -> Result<()> {
        let target = self.command("Target.createTarget", json!({"url": "about:blank"}))?;
        self.target = target["targetId"].clone();
        let attached = self.command(
            "Target.attachToTarget",
            json!({"targetId":self.target,"flatten":true}),
        )?;
        self.command("Target.activateTarget", json!({"targetId":self.target}))?;
        self.session = Some(
            attached["sessionId"]
                .as_str()
                .ok_or("Missing browser session")?
                .into(),
        );
        self.command("Page.navigate", json!({"url":url}))?;
        Self::settle();
        Ok(())
    }

    pub fn follow_active_tab(&mut self) -> Result<()> {
        let previous = self.session.take();
        let targets = self.command("Target.getTargets", json!({}))?;
        for target in targets["targetInfos"]
            .as_array()
            .ok_or("Missing browser tabs")?
            .iter()
            .take(32)
        {
            if target["type"] != "page" {
                continue;
            }
            let attached = self.command(
                "Target.attachToTarget",
                json!({"targetId":target["targetId"],"flatten":true}),
            )?;
            let session = attached["sessionId"]
                .as_str()
                .ok_or("Missing browser session")?
                .to_owned();
            self.session = Some(session.clone());
            let focus = self.command(
                "Runtime.evaluate",
                json!({"expression":"document.hasFocus()","returnByValue":true}),
            )?;
            if focus["result"]["value"] == true {
                self.target = target["targetId"].clone();
                if let Some(old) = previous {
                    self.session = None;
                    self.command("Target.detachFromTarget", json!({"sessionId":old}))?;
                    self.session = Some(session);
                }
                return Ok(());
            }
            self.session = None;
            self.command("Target.detachFromTarget", json!({"sessionId":session}))?;
        }
        self.session = previous;
        self.command("Target.activateTarget", json!({"targetId":self.target}))?;
        Ok(())
    }

    pub fn save_profile(&mut self) -> Result<()> {
        self.follow_active_tab()?;
        let info = self.command("Target.getTargetInfo", json!({"targetId":self.target}))?;
        let url = info["targetInfo"]["url"]
            .as_str()
            .unwrap_or("about:blank")
            .to_owned();
        self.session = None;
        let _ = self.command("Browser.close", json!({}));
        for _ in 0..100 {
            if self.child.try_wait()?.is_some() {
                break;
            }
            thread::sleep(Duration::from_millis(50));
        }
        if self.child.try_wait()?.is_none() {
            return Err("Browser did not close cleanly".into());
        }
        let directory = self.directory.clone();
        let archive = directory.join("profile.tar.gz");
        let status = Command::new("tar")
            .args(["-czf"])
            .arg(&archive)
            .args([
                "-C",
                "/home/workflow/.local/share/placecontext-browser",
                "--exclude=Singleton*",
                "--exclude=*Cache*",
                "--exclude=Crashpad",
                "--exclude=BrowserMetrics*",
                "--exclude=ShaderCache",
                "--exclude=GrShaderCache",
                "--exclude=*.tmp",
                ".",
            ])
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status();
        *self = Self::launch(&directory)?;
        self.open(&url)?;
        if !status?.success() || std::fs::metadata(&archive)?.len() > 8 * 1024 * 1024 {
            let _ = std::fs::remove_file(archive);
            return Err("Browser session snapshot failed or is too large".into());
        }
        Ok(())
    }

    pub fn inspect(&mut self) -> Result<Value> {
        let info = self.command("Target.getTargetInfo", json!({"targetId":self.target}))?;
        Ok(
            json!({"url":info["targetInfo"]["url"],"title":info["targetInfo"]["title"],"width":1280,"height":800}),
        )
    }

    fn desktop(args: &[&str]) -> Result<()> {
        if !Command::new("xdotool")
            .args(args)
            .env("LANG", "C.UTF-8")
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()?
            .success()
        {
            return Err("Desktop input failed".into());
        }
        Ok(())
    }

    fn move_pointer(&mut self, x: f64, y: f64) -> Result<()> {
        let geometry = Command::new("xdotool").arg("getdisplaygeometry").output()?;
        let size = String::from_utf8(geometry.stdout)?;
        let dimensions: Vec<f64> = size
            .split_whitespace()
            .map(str::parse)
            .collect::<std::result::Result<_, _>>()?;
        if !geometry.status.success()
            || dimensions.len() != 2
            || dimensions
                .iter()
                .any(|n| !n.is_finite() || *n < 1.0 || *n > 4096.0)
        {
            return Err("Invalid desktop geometry".into());
        }
        let (from_x, from_y) = self.pointer;
        for step in 1..=10 {
            let t = f64::from(step) / 10.0;
            let px = ((from_x + (x - from_x) * t) * dimensions[0] / 1280.0)
                .floor()
                .to_string();
            let py = ((from_y + (y - from_y) * t) * dimensions[1] / 800.0)
                .floor()
                .to_string();
            Self::desktop(&["mousemove", &px, &py])?;
            thread::sleep(Duration::from_millis(50));
        }
        self.pointer = (x, y);
        Ok(())
    }

    fn click(&mut self, x: f64, y: f64) -> Result<()> {
        self.move_pointer(x, y)?;
        Self::desktop(&["click", "1"])
    }

    pub fn act(&mut self, proposal: &Value) -> Result<()> {
        let x = proposal["x"].as_f64().unwrap_or(640.0);
        let y = proposal["y"].as_f64().unwrap_or(400.0);
        match proposal["action"].as_str().ok_or("Missing visual action")? {
            "click" => self.click(x, y)?,
            "type" => {
                self.click(x, y)?;
                Self::desktop(&["key", "--clearmodifiers", "ctrl+a"])?;
                Self::desktop(&[
                    "type",
                    "--clearmodifiers",
                    "--delay",
                    "1",
                    "--",
                    proposal["text"].as_str().ok_or("Missing field text")?,
                ])?;
            }
            "enter" => Self::desktop(&["key", "--clearmodifiers", "Return"])?,
            "scroll_down" | "scroll_up" => {
                self.move_pointer(x, y)?;
                Self::desktop(&[
                    "click",
                    "--repeat",
                    "5",
                    "--delay",
                    "50",
                    match proposal["action"].as_str() {
                        Some("scroll_down") => "5",
                        _ => "4",
                    },
                ])?;
            }
            "back" => Self::desktop(&["key", "--clearmodifiers", "alt+Left"])?,
            "wait" => {}
            _ => return Err("Unsupported desktop action".into()),
        }
        Ok(())
    }

    pub fn settle() {
        thread::sleep(Duration::from_secs(1));
    }
}

impl Drop for Browser {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pipe_framing_handles_multiple_messages_and_rejects_bad_or_oversized_frames() {
        let mut reader = BufReader::with_capacity(3, &b"{\"id\":1}\0{\"id\":2}\0"[..]);
        assert_eq!(read_frame(&mut reader).unwrap()["id"], 1);
        assert_eq!(read_frame(&mut reader).unwrap()["id"], 2);
        assert!(read_frame(&mut reader).is_err());
        assert!(read_frame(&mut &b"invalid\0"[..]).is_err());
        assert!(read_frame(&mut &vec![b'x'; 2_097_153][..]).is_err());
    }
}

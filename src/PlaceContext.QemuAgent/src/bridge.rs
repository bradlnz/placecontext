use serde_json::{json, Value};
use std::{
    fs,
    io::{self, Read},
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};

pub type Result<T> = std::result::Result<T, Box<dyn std::error::Error + Send + Sync>>;

pub fn read_json(path: &Path, limit: usize) -> Result<Value> {
    let mut bytes = Vec::new();
    fs::File::open(path)?
        .take(limit as u64 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > limit {
        return Err("JSON size limit exceeded".into());
    }
    Ok(serde_json::from_slice(&bytes)?)
}

pub fn publish(directory: &Path, name: &str, value: &Value) -> Result<()> {
    let bytes = serde_json::to_vec(value)?;
    if bytes.len() > 131_072 {
        return Err("Guest output size limit exceeded".into());
    }
    let temporary = directory.join(format!("{name}.tmp"));
    fs::write(&temporary, bytes)?;
    fs::rename(temporary, directory.join(format!("{name}.json")))?;
    Ok(())
}

pub struct Bridge {
    directory: PathBuf,
    sequence: u32,
}

impl Bridge {
    pub fn new(directory: &Path) -> Self {
        Self {
            directory: directory.into(),
            sequence: 0,
        }
    }

    pub fn ask(&mut self, kind: &str, body: Value) -> Result<Value> {
        self.sequence += 1;
        publish(
            &self.directory,
            "request",
            &json!({"id": self.sequence, "kind": kind, "body": body}),
        )?;
        let response = self.directory.join("response.json");
        let deadline = Instant::now() + Duration::from_secs(if kind == "control" { 3600 } else { 100 });
        while Instant::now() < deadline {
            match read_json(&response, 131_072) {
                Ok(value) => {
                    fs::remove_file(&response)?;
                    if value["id"].as_u64() != Some(self.sequence.into())
                        || value.get("body").is_none()
                    {
                        return Err("Unexpected host response".into());
                    }
                    return Ok(value["body"].clone());
                }
                Err(error)
                    if error
                        .downcast_ref::<io::Error>()
                        .is_some_and(|e| e.kind() == io::ErrorKind::NotFound) => {}
                Err(error) => return Err(error),
            }
            thread::sleep(Duration::from_millis(50));
        }
        Err("Host response timed out".into())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn file_bridge_checks_sequence_and_bounds() {
        let directory = std::env::temp_dir().join(format!("pc-jev-test-{}", std::process::id()));
        fs::create_dir(&directory).unwrap();
        publish(
            &directory,
            "response",
            &json!({"id": 1, "body": {"model":"test"}}),
        )
        .unwrap();
        let mut bridge = Bridge::new(&directory);
        assert_eq!(bridge.ask("jev", json!({})).unwrap()["model"], "test");
        assert_eq!(
            read_json(&directory.join("request.json"), 131072).unwrap()["id"],
            1
        );
        assert!(read_json(&directory.join("request.json"), 3).is_err());
        publish(&directory, "response", &json!({"id": 7, "body": {}})).unwrap();
        assert!(bridge.ask("jev", json!({})).is_err());
        assert!(publish(&directory, "large", &json!("x".repeat(131072))).is_err());
        fs::remove_dir_all(directory).unwrap();
    }
}

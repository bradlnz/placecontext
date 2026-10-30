mod agent;
mod bridge;
mod browser;

use bridge::{publish, read_json, Bridge, Result};
use browser::Browser;
use serde_json::json;
use std::{path::PathBuf, thread, time::Duration};

fn main() {
    if let Err(error) = execute() {
        // Error strings originate in our bridge, never in provider responses or page text.
        eprintln!("Jev guest failed: {error}");
        std::process::exit(1);
    }
}

fn execute() -> Result<()> {
    let directory = PathBuf::from(
        std::env::args_os()
            .nth(1)
            .ok_or("Task directory required")?,
    );
    let config = read_json(&directory.join("config.json"), 524_288)?;
    agent::validate_config(&config)?;
    let mut browser = match Browser::launch(&directory) {
        Ok(browser) => browser,
        Err(_) => {
            publish(&directory, "result", &json!({"status":"error", "steps":[]}))?;
            return Ok(());
        }
    };
    let result = agent::run(&config, &mut browser, &mut Bridge::new(&directory));
    publish(&directory, "result", &result)?;
    // Keep the final page visible for replay; the host owns VM timeout and teardown.
    loop {
        thread::sleep(Duration::from_secs(60));
    }
}

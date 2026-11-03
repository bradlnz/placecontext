use crate::{
    bridge::{Bridge, Result},
    browser::Browser,
};
use serde_json::{json, Map, Value};

struct Judgment {
    choice: String,
    confidence: f64,
    goal: f64,
    stuck: f64,
}

fn probability(value: &Value) -> Result<f64> {
    value
        .as_f64()
        .filter(|v| v.is_finite() && (0.0..=1.0).contains(v))
        .ok_or_else(|| "Invalid probability".into())
}

fn validate(reply: &Value, criteria: &Map<String, Value>) -> Result<Judgment> {
    let answers = &reply["answers"];
    let action = &answers["action"];
    let choice = action["choice"].as_str().ok_or("Missing choice")?;
    let probabilities = action["probabilities"]
        .as_object()
        .ok_or("Missing probabilities")?;
    if reply["model"]
        .as_str()
        .is_none_or(|model| model.trim().is_empty())
        || action["type"] != "choice"
        || !criteria.contains_key(choice)
        || probabilities.len() != criteria.len()
        || answers["goal_done"]["type"] != "noul"
        || answers["stuck"]["type"] != "noul"
    {
        return Err("Invalid Jev judgment".into());
    }
    let selected = probability(&action["probabilities"][choice])?;
    let mut sum = 0.0;
    for key in criteria.keys() {
        let p = probability(&action["probabilities"][key])?;
        if p > selected + 0.000001 {
            return Err("Choice is not a maximum".into());
        }
        sum += p;
    }
    if (sum - 1.0).abs() > 0.01 {
        return Err("Invalid probability distribution".into());
    }
    Ok(Judgment {
        choice: choice.into(),
        confidence: probability(&action["confidence"])?,
        goal: probability(&answers["goal_done"]["noul"])?,
        stuck: probability(&answers["stuck"]["noul"])?,
    })
}

pub fn validate_config(config: &Value) -> Result<()> {
    let task = config["task"].as_str().ok_or("Missing task")?;
    let context = config["context"].as_str().ok_or("Missing context")?;
    let url = config["startUrl"].as_str().ok_or("Missing URL")?;
    if task.trim().is_empty()
        || task.chars().count() > 8192
        || task.contains('\0')
        || context.chars().count() > 65_536
        || !(url.starts_with("http://") || url.starts_with("https://"))
        || !config["maxSteps"]
            .as_u64()
            .is_some_and(|n| (1..=100).contains(&n))
    {
        return Err("Invalid task configuration".into());
    }
    Ok(())
}

fn proposal(reply: &Value) -> Result<Value> {
    let text = reply["choices"][0]["message"]["content"]
        .as_str()
        .filter(|s| s.len() <= 16384)
        .ok_or("Missing visual proposal")?;
    let value: Value = serde_json::from_str(text)?;
    let action = value["action"].as_str().ok_or("Missing visual action")?;
    if ![
        "click",
        "type",
        "enter",
        "scroll_down",
        "scroll_up",
        "back",
        "wait",
        "request_human",
        "done",
    ]
    .contains(&action)
        || value["description"]
            .as_str()
            .is_none_or(|s| s.is_empty() || s.len() > 200)
        || value["observation"].as_str().is_none_or(|s| s.len() > 8192)
    {
        return Err("Invalid visual proposal".into());
    }
    if ["click", "type", "scroll_down", "scroll_up"].contains(&action) {
        for (name, bound) in [("x", 1280.0), ("y", 800.0)] {
            if value[name]
                .as_f64()
                .is_none_or(|n| !n.is_finite() || n < 0.0 || n >= bound)
            {
                return Err("Invalid visual coordinates".into());
            }
        }
    }
    if action == "type"
        && value["text"]
            .as_str()
            .is_none_or(|s| s.is_empty() || s.chars().count() > 4000 || s.contains('\0'))
    {
        return Err("Invalid field text".into());
    }
    Ok(value)
}

fn checkpoint(
    config: &Value,
    browser: &mut Browser,
    bridge: &mut Bridge,
    pause: bool,
) -> Result<bool> {
    if config["interactive"] != true {
        return Ok(false);
    }
    let mut body = json!({"pause":pause});
    loop {
        let reply = bridge.ask("control", body)?;
        match reply["action"].as_str() {
            Some("continue") => return Ok(false),
            Some("resumed") => {
                browser.follow_active_tab()?;
                return Ok(true);
            }
            Some("save") => {
                body = json!({"saved":browser.save_profile().is_ok()});
            }
            _ => return Err("Invalid desktop handoff".into()),
        }
    }
}

pub fn run(config: &Value, browser: &mut Browser, bridge: &mut Bridge) -> Value {
    let mut steps = Vec::<Value>::new();
    let mut page = Value::Null;
    let mut observation = Value::Null;
    let mut visual_evidence = Value::Null;
    let outcome = (|| -> Result<&str> {
        validate_config(config)?;
        browser.open(config["startUrl"].as_str().unwrap())?;
        for step in 1..=config["maxSteps"].as_u64().unwrap() {
            checkpoint(config, browser, bridge, false)?;
            page = browser.inspect()?;
            let state = json!({"task":config["task"],"context":config["context"],"page":page,
                "history":&steps[steps.len().saturating_sub(6)..]});
            let candidate = proposal(&bridge.ask("vision", json!({"state":state}))?)?;
            if candidate["action"] == "request_human" {
                if config["interactive"] != true {
                    return Err("Human login requires interactive mode".into());
                }
                checkpoint(config, browser, bridge, true)?;
                continue;
            }
            observation = candidate["observation"].clone();
            let criteria = json!({"act":include_str!("../prompts/approve-action.md"),
                "wait":include_str!("../prompts/wait-action.md"),
                "done":include_str!("../prompts/completed-action.md")})
            .as_object()
            .unwrap()
            .clone();
            let mut questions = json!({
                "action":{"type":"choice","instructions":include_str!("../prompts/review-action.md"),"criteria":criteria},
                "goal_done":{"type":"noul","instructions":include_str!("../prompts/review-completion.md")},
                "stuck":{"type":"noul","instructions":include_str!("../prompts/review-progress.md")}
            });
            if candidate["action"] == "done" && candidate["offer"].is_object() {
                for (key, instruction) in [
                    ("price", include_str!("../prompts/review-price.md")),
                    ("pack", include_str!("../prompts/review-pack.md")),
                    ("match", include_str!("../prompts/review-product.md")),
                    ("location", include_str!("../prompts/review-location.md")),
                    (
                        "available",
                        include_str!("../prompts/review-availability.md"),
                    ),
                ] {
                    questions[key] = json!({"type":"noul","instructions":instruction});
                }
            }
            let reply = bridge.ask("jev",json!({"state":{"task":config["task"],"context":config["context"],
                "screenshot":"screen-image","page":page,"proposal":candidate,"offer":candidate["offer"],
                "history":&steps[steps.len().saturating_sub(6)..]},"questions":questions}))?;
            let judgment = validate(&reply, &criteria)?;
            if checkpoint(config, browser, bridge, false)? {
                continue;
            }
            steps.push(
                json!({"step":step,"proposed_action":candidate["action"],"executed_action":null,
                "confidence":judgment.confidence,"goal_done":judgment.goal,"stuck":judgment.stuck,
                "detail":candidate["description"],"judgment":judgment.choice}),
            );
            if candidate["action"] == "done" {
                if candidate["offer"].is_object() {
                    let mut checks = Map::new();
                    for key in ["price", "pack", "match", "location", "available"] {
                        if reply["answers"][key]["type"] != "noul" {
                            return Err("Invalid visual evidence judgment".into());
                        }
                        checks.insert(
                            key.into(),
                            json!(probability(&reply["answers"][key]["noul"])?),
                        );
                    }
                    visual_evidence = json!({"source":"screenshot","offer":candidate["offer"],"confidence":checks});
                }
                if judgment.choice == "done" && judgment.goal > 0.85 {
                    return Ok("goal_achieved");
                }
                return Ok("unverified");
            }
            if step > 2 && judgment.stuck > 0.85 {
                return Ok("stuck");
            }
            let trace = steps.last_mut().unwrap();
            match judgment.choice.as_str() {
                "wait" => trace["executed_action"] = json!("wait"),
                "act" => match browser.act(&candidate) {
                    Ok(_) => trace["executed_action"] = candidate["action"].clone(),
                    Err(_) => {
                        trace["action_error"] =
                            json!("Browser action failed; inspect the next screenshot.")
                    }
                },
                _ => {
                    trace["action_error"] =
                        json!("Jev did not approve the proposed action; inspect again.")
                }
            }
            Browser::settle();
        }
        Ok("max_steps")
    })();
    let error = outcome.as_ref().err().map(|e| e.to_string());
    json!({"status":outcome.unwrap_or("error"),"error":error,"final_url":page["url"],"final_title":page["title"],
        "page":observation,"visual_evidence":visual_evidence,"steps":steps})
}

#[cfg(test)]
mod tests {
    use super::*;
    fn reply() -> Value {
        json!({"model":"test", "answers": {
        "action":{"type":"choice", "choice":"click", "confidence":1.0, "probabilities":{"click":1.0,"done":0.0}},
        "goal_done":{"type":"noul","noul":0.0}, "stuck":{"type":"noul","noul":0.0}}})
    }
    fn criteria() -> Map<String, Value> {
        json!({"click":"Click", "done":"Done"})
            .as_object()
            .unwrap()
            .clone()
    }
    #[test]
    fn validates_all_scores_choices_and_distributions() {
        assert!(validate(&reply(), &criteria()).is_ok());
        for (pointer, value) in [
            ("/answers/action/choice", json!("host_shell")),
            ("/answers/action/probabilities/click", json!(0.2)),
            ("/answers/action/confidence", Value::Null),
            ("/answers/goal_done/noul", json!(2)),
            ("/answers/stuck/type", json!("choice")),
            ("/model", json!("")),
        ] {
            let mut r = reply();
            *r.pointer_mut(pointer).unwrap() = value;
            assert!(validate(&r, &criteria()).is_err());
        }
        let mut r = reply();
        r["answers"]["action"]["probabilities"]["evil"] = json!(0);
        assert!(validate(&r, &criteria()).is_err());
        r = reply();
        r["answers"]["action"]["probabilities"] = json!({"click":0.4,"done":0.6});
        assert!(validate(&r, &criteria()).is_err());
    }
    #[test]
    fn validates_visual_proposals_and_task_limits() {
        let mut config = json!({"task":"Order","context":"{}","startUrl":"http://orders.test","maxSteps":20,"typing":true});
        assert!(validate_config(&config).is_ok());
        config["maxSteps"] = json!(101);
        assert!(validate_config(&config).is_err());
        let valid = json!({"action":"type","description":"Fill search","observation":"Search field","x":100,"y":200,"text":"4101"});
        let response = |v: &Value| json!({"choices":[{"message":{"content":v.to_string()}}]});
        assert!(proposal(&response(&valid)).is_ok());
        for (key, value) in [
            ("action", json!("shell")),
            ("x", json!(-1)),
            ("y", json!(800)),
            ("text", json!("")),
        ] {
            let mut invalid = valid.clone();
            invalid[key] = value;
            assert!(proposal(&response(&invalid)).is_err());
        }
    }
}

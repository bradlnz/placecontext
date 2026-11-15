"""Link the UI-created project agents to the isolated demo workflow."""
import json
from seed import OUT, DB, psql, literal

config = json.loads((OUT / "project.json").read_text())
agents = json.loads((OUT / "agents.json").read_text())
by_name = {a["name"]: a["id"] for a in agents}
stages = json.loads(psql(DB, f'SELECT "StagesJson" FROM job_chains WHERE "Id"={literal(config["workflow"])} AND "ProjectId"={literal(config["project"])};'))
for index, name in [(2, "Triage"), (3, "Routing Reviewer"), (8, "Handoff Writer")]:
    assert stages[index]["action"]["type"] == "agentTask"
    stages[index]["action"]["agentId"] = by_name[name]
psql(DB, f'UPDATE job_chains SET "StagesJson"={literal(stages)}, "UpdatedAt"=now() WHERE "Id"={literal(config["workflow"])} AND "ProjectId"={literal(config["project"])};')
print("Linked Triage, Routing Reviewer and Handoff Writer to their real workflow steps.")

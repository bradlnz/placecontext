# Services demo

Build with `docker build -t placecontext-service-demo:local examples/service-demo`
and make the image available to your Kubernetes cluster. Launch it in **Services**
on port **8080**, then create a Redis store in **Infrastructure → Databases** and
select this app as its owner. The app restarts with its private connection variables.
Open the app name for details, logs, and its database, or use **Open app** to visit it.
The app stores a visit counter and writes JSON log events.

This is a demonstration, not a production Redis client: it supports only the two
commands it uses and opens a connection per page visit.

1. That flatten json process we need to remove, we need to instead make all the project tables rows in mongodb this way we can store unstructured data better. We still need to have relational data everything will get pinned by X column, ie address.
2. We still need the analytical ability so I am thinking we using something like opensearch - users need to be able to query there data and build analytical reporting.

Jobs -> Build data -> Ingest -> Build reporting and understanding.
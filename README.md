##CosmosDB multiget load test

This solution is built to load test Cosmos DB collection. The collection under load has 25 partitions with average document size of about 10K and provisioned RUs of 250K.

# WebService - an httpListener-based barebones service
* Needs to be executed with Admin rights to bind to the port
* Exposes test data POST endpoint (http://localhost:9000/test_data) that takes an integer as text as an input and returns requested number of keys from target collection
* and MGet POST endpoint (http://localhost:9000/req) that takes a string with comma-separated keys to fetch
* Cosmos DB collection details are defined in Client.fs

#DDOS - a client for WebService 
It fetches test data, randomly selects BATCH number of keys, and opens fire-and-forget requests to MGet endpoint sleeping between calls as defined by `tests`. 
At the rate specified (10,000 requests with 5 ms sleep between) WebService starts reporting disconnect exceptions from CosmosDB after about a minute on my machine.
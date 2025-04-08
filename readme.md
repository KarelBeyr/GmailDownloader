This is an experimental project that aims to embed (in laymen term "index") emails of given gmail inbox and then look for similarities.

The pipeline works like this:
1. GmailDownloader (C#): Download all email conversations (threads) to disk. Currently they are chunked by year+month and stored in .JSON file
2. Embedder (Python script): Run  script that listens on port 8000 and has a single POST endpoint that accepts string and returns vector of floats
3. EmailEmbedder (C#): Go through all .JSON files and for each thread find first email body, embed it using calling python script and save it in output .JSON file.
4. API (C#): Simple REST API app that has single page where you can enter text, then it would call python script to embed it and then it would find three most similar emails and render links to them

How to setup python script (https://chatgpt.com/c/67f39695-4770-8013-81c5-d5f948151712)
1. pip install sentence-transformers transformers accelerate huggingface-hub
2. // optional pip install --upgrade sentence-transformers transformers accelerate huggingface-hub
3. python embedder.Python
4. test_embedder.bat

How to setup GmailDownloader (https://chatgpt.com/c/67f2dc66-26b0-8013-a3c6-72ea919ce9f2)
1. Set the project as startup in visual studio and run it
2. Create gmail API for your account
3. Browser will popup with logged in gmail accounts, choose the one that you added as test account to download emails from it
4. Result files will be in bin folder

How to setup EmailEmbedder
1. Copy .JSON files to a known location and set this as target
2. Set the project as startup in visual studio and run it
3. It will call python script for each thread to get embedding of first email of each thread and store it in result file next to input file

How to setup API app
1. Set the project as startup in visual studio and run it
2. It will run on random port (maybe on https)
3. Insert your email text there and press button. It will render links to three most similar threads
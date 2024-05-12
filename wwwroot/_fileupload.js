async function uploadFile(e) {
    // disable the default form action
    e.preventDefault();
    let fn = 'uploadFile'; console.debug(fn);

    // get the file input element
    let fileSelect = document.getElementById("uploadfileselect");
    if(fileSelect == null) { return; }    
    let fileName = fileSelect.value;
    console.debug(fn, 'fileName:', fileName);
    if(fileSelect.files.length == 0 || fileName == null || fileName =='') {
        d("No file selected; nothing to upload!");
        c(RC.ERROR);
        return;
    }

    let aftoken = null;
    let bail = false;
    apiUrl = '/antiforgerytoken';
    console.debug(`${fn} --> ${apiUrl}`);
    await fetch(apiUrl, {
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        },
        credentials: 'include'
    })
        .then(handleResponse)
        .then(response => response.json())
        .then(json => {
            aftoken = json;
            console.debug(`${fn} -- aftoken: ${aftoken.requestToken}`)
            return aftoken;
        })
        .catch(error => {
            console.error('Error:', error);
            d('Error: ' + error);
            c(RC.ERROR);
            bail = true;
        });
    if(bail) {
        return;
    }

    
    console.info(`${fn} -- aftoken: >>${aftoken.requestToken}<<`);
    // Call the REST API
    

    let file = fileSelect.files[0];
    let data = new FormData();
    data.append('file', file);
    apiUrl = '/fileuploadxfer';
    console.debug(`${fn} --> ${apiUrl}`);
    let apiMethod = 'POST';
    await fetch(apiUrl, {
        method: apiMethod,
        headers: {
            "GeFeSLE-XMLHttpRequest": "true",
            'RequestVerificationToken': aftoken.requestToken
        },
        credentials: 'include',
        body: data
    })
        .then(handleResponse)
        .then(response => response.text())
        .then(data => {
            let returnFileName = data;
            console.log('returning file name: ' + returnFileName);
            returnFileName = returnFileName.replace(/"/g, ''); // remove quotation marks
            let oldval = easymde.value();
            console.debug('oldval: ' + oldval);
            let newval = oldval + ' ![receipt](' + returnFileName + ')';
            easymde.value(newval);
            return returnFileName;
            
        })
        .catch(error => {
            console.error(error);
            d('Error: ' + error);
            c(RC.ERROR);
            return null;
        });

}
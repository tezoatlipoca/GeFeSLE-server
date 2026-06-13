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

    let aftoken = localStorage.getItem('antiForgeryToken');
    let afHeaderName = localStorage.getItem('antiForgeryHeaderName') || 'RequestVerificationToken';

    // If token isn't cached yet (e.g. OAuth login path), refresh it from /me
    if (!aftoken) {
        apiUrl = '/me/';
        console.debug(`${fn} --> ${apiUrl} (refresh antiforgery token)`);
        await fetch(apiUrl, {
            headers: {
                "GeFeSLE-XMLHttpRequest": "true"
            },
            credentials: 'include'
        })
            .then(handleResponse)
            .then(response => response.json())
            .then(json => {
                if (json && json.antiForgeryToken) {
                    aftoken = json.antiForgeryToken;
                    localStorage.setItem('antiForgeryToken', aftoken);
                }
                if (json && json.antiForgeryHeaderName) {
                    afHeaderName = json.antiForgeryHeaderName;
                    localStorage.setItem('antiForgeryHeaderName', afHeaderName);
                }
            })
            .catch(error => {
                console.error('Error:', error);
                d('Error: ' + error);
                c(RC.ERROR);
            });
    }

    if (!aftoken) {
        d('Missing antiforgery token. Please log in again.');
        c(RC.UNAUTHORIZED);
        return;
    }

    console.info(`${fn} -- aftoken: >>${aftoken}<<`);
    // Call the REST API
    

    let file = fileSelect.files[0];
    let data = new FormData();
    data.append('file', file);
    apiUrl = '/files';
    console.debug(`${fn} --> ${apiUrl}`);
    let apiMethod = 'POST';
    const headers = {
        "GeFeSLE-XMLHttpRequest": "true"
    };
    headers[afHeaderName] = aftoken;

    await fetch(apiUrl, {
        method: apiMethod,
        headers: headers,
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
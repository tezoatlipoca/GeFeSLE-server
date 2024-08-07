// define the onClick event handler function for the indexregenlink link
async function interceptRegen(event) {
    // prevent the default action of the link
    event.preventDefault();
    console.debug(' interceptRegen');

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let regenlink = document.getElementById('indexregenlink');
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/index.html'));

    // Get the list data from the API
    apiUrl = apiUrl + '/regenerate';
    console.debug(' | REGEN - apiUrl: ', apiUrl);
    // fetch the /regenerate endpoint; the result will be some text; 
    // put the text into an alert
    // also returns: 
    await fetch(apiUrl, {
        method: 'GET',
        headers: {
            "GeFeSLE-XMLHttpRequest": "true"
        }
    })
        .then(response => {
            console.debug(' | REGEN - Response: ', response);
            if (response.status == RC.UNAUTHORIZED) {
                throw new Error('Not authorized! Have you logged in yet <a href=\"_login.html\">(click here)?</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                throw new Error('Forbidden! Are you logged in as an listowner?');

            }
            else {
                return response.text();
            }


        })
        .then((text) => {
            d(text);
            c(RC.OK);
            // wait 10 seconds and refresh the page
            setTimeout(() => { location.reload(); }, 10000);
        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });

}

window.onload = async function () {
    let fn = 'index.js - window.onload';
    console.debug(fn);

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }

    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (username != null) {
        console.debug(fn + ' | logged in');
        links = document.getElementsByClassName('pwdchangelink');
        for (let l of links) { l.style.display = ''; }
        links = document.getElementsByClassName('loginlink');
        for (let l of links) { l.style.display = 'none'; }
    }
    else {
        console.debug(fn + ' | not logged in');
        links = document.getElementsByClassName('pwdchangelink');
        for (let l of links) { l.style.display = 'none'; }
        links = document.getElementsByClassName('loginlink');
        for (let l of links) { l.style.display = ''; }
    }
    if (isSuperUser(role) || isListOwner(role)) {
        console.debug(fn + ' | logged in and either isSuperUser or isListOwner');
        // SHOW the id=indexeditlink
        let links = document.getElementsByClassName('indexeditlink');
        for (l of links) {
            l.style.display = '';
        }
        showAdminSecrets();
    }
    showDebuggingElements();

    // by default all lists - which are <li class="indexliitem"> - are hidden
    // except the Public ones which visible by default. 
    // obtain the list of lists the user can see and make visible the ones 
    // they're allowed to see
    var lists = await getLists();
    // lists is a json glob which is a lot more information about each list than we need. 
    // we just need the NAME of the list
    if (lists == null) {
        console.debug('lists is null');
        return;
    }
    else {
        lists = lists.map((l) => l.name);
        console.debug('lists:', lists);
        // now iterate through all the list bullets and make visible the ones
        // where the NAME in the <a> tag is in the lists array (but it has to match exact)
        var listItems = document.getElementsByClassName('indexliitem');
        for (let li of listItems) {
            let listName = li.getElementsByTagName('a')[0].innerText;
            if (lists.includes(listName)) {
                li.style.display = '';
            }
        }
    }

}


function deleteList(listId) {
    let fn = "deleteList"; console.debug(`${fn} - ${listId}`);
    if (islocal()) return;
    if (confirm('Are you sure you want to delete this LIST?')) {
        let apiUrl = '/lists/' + listId;
        console.debug(`${fn} --> ${apiUrl}`);
        fetch(apiUrl, {
            method: 'DELETE',
            headers: {
                'Content-Type': 'application/json',
                "GeFeSLE-XMLHttpRequest": "true"
            },
        })
            .then(response => {
                console.log('Response IS:', response);
                // if the response is ok, redirect to the list page
                if (response.ok) {
                    // save ourselves a result message in localstorage
                    localStorage.setItem('result', 'result ' + listId + ' deleted successfully');

                    // just refresh the page
                    location.reload();
                }
                else {
                    throw new Error(`${fn} <-- ${response.status} - ${response.statusText} - ${response.url}`);
                }

            })
            .catch((error) => {
                console.error(error);
                d(error);
                c(RC.ERROR);
            });
    }
}
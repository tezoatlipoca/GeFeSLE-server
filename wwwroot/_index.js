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

    // index.html contains only public lists.
    // Fetch authorized lists and append any additional non-public lists.
    var lists = await getLists();
    if (lists == null) {
        console.debug('lists is null');
        return;
    }
    else {
        console.debug('lists:', lists);
        let listRoot = document.getElementById('indexuloflists');
        if (!listRoot) {
            console.debug('index list root not found');
            return;
        }

        // Track what static index already contains (public lists).
        let existingNames = new Set();
        let listItems = document.getElementsByClassName('indexliitem');
        for (let li of listItems) {
            let anchor = li.getElementsByTagName('a')[0];
            if (anchor && anchor.innerText) {
                existingNames.add(anchor.innerText);
            }
        }

        for (let list of lists) {
            if (!list || !list.name || existingNames.has(list.name)) {
                continue;
            }

            let li = document.createElement('li');
            li.className = 'indexliitem runtime-authorized-list';
            li.setAttribute('data-render-source', 'runtime-authorized');
            li.setAttribute('data-list-name', list.name);
            li.setAttribute('data-list-id', list.id);

            let listUrl = encodeURI(list.name + '.html');
            li.innerHTML = '<a href="' + listUrl + '">' + escapeHtml(list.name) + '</a>' +
                ' <span class="indexeditlink" style="display: none;"><a href="_edit.list.html?listid=' + list.id + '">Edit</a></span>' +
                ' <span class="indexeditlink" style="display: none;"><a href="#" onclick="deleteList(' + list.id + '); return;">Delete</a></span>';

            listRoot.appendChild(li);
            existingNames.add(list.name);

            if (isSuperUser(role) || isListOwner(role)) {
                let links = li.getElementsByClassName('indexeditlink');
                for (let l of links) {
                    l.style.display = '';
                }
            }
        }

        let noLists = document.getElementById('nolists');
        if (noLists && existingNames.size > 0) {
            noLists.remove();
        }
    }

}

function escapeHtml(input) {
    if (!input) return '';
    return input
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
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
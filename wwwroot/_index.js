function normalizeIdentityValue(value) {
    if (typeof value !== "string") {
        return "";
    }
    return value.trim().toLowerCase();
}

function getUserIdentitySet(sessionData) {
    const identities = new Set();
    const addIdentity = (candidate) => {
        const normalized = normalizeIdentityValue(candidate);
        if (normalized.length > 0) {
            identities.add(normalized);
        }
    };

    if (!sessionData || typeof sessionData !== "object") {
        return identities;
    }

    addIdentity(sessionData.id);
    addIdentity(sessionData.userId);
    addIdentity(sessionData.userid);
    addIdentity(sessionData.userName);
    addIdentity(sessionData.username);
    addIdentity(sessionData.email);
    if (sessionData.user && typeof sessionData.user === "object") {
        addIdentity(sessionData.user.id);
        addIdentity(sessionData.user.userName);
        addIdentity(sessionData.user.username);
        addIdentity(sessionData.user.email);
    }
    return identities;
}

function objectMatchesIdentity(entity, identitySet) {
    if (!entity || typeof entity !== "object" || !identitySet || identitySet.size === 0) {
        return false;
    }
    const candidates = [
        entity.id,
        entity.userId,
        entity.userid,
        entity.userName,
        entity.username,
        entity.email
    ];
    return candidates.some((candidate) => identitySet.has(normalizeIdentityValue(candidate)));
}

function collectionMatchesIdentity(collection, identitySet) {
    if (!Array.isArray(collection) || collection.length === 0) {
        return false;
    }
    return collection.some((entry) => {
        if (typeof entry === "string") {
            return identitySet.has(normalizeIdentityValue(entry));
        }
        return objectMatchesIdentity(entry, identitySet);
    });
}

function getHighestListPermissionLabel(list, identitySet) {
    if (!list || !identitySet || identitySet.size === 0) {
        return null;
    }

    const creatorFlags = [list.isCreator, list.iscreator, list.amICreator, list.amIcreator, list.userIsCreator];
    if (creatorFlags.some((flag) => flag === true)) {
        return "creator/owner";
    }

    const ownerFlags = [list.isListOwner, list.islistowner, list.amIListOwner, list.amIlistowner, list.userIsListOwner];
    if (ownerFlags.some((flag) => flag === true)) {
        return "listowner";
    }

    const contributorFlags = [list.isContributor, list.iscontributor, list.amIContributor, list.amIcontributor, list.userIsContributor];
    if (contributorFlags.some((flag) => flag === true)) {
        return "contributor";
    }

    const creatorMatches = objectMatchesIdentity(list.creator, identitySet);
    if (creatorMatches) {
        return "creator/owner";
    }

    if (identitySet.has(normalizeIdentityValue(list.creatorId))
        || identitySet.has(normalizeIdentityValue(list.creatorUserId))
        || identitySet.has(normalizeIdentityValue(list.creatorUserName))
        || identitySet.has(normalizeIdentityValue(list.creatorUsername))) {
        return "creator/owner";
    }

    if (collectionMatchesIdentity(list.listOwners, identitySet)
        || collectionMatchesIdentity(list.listowners, identitySet)
        || collectionMatchesIdentity(list.owners, identitySet)) {
        return "listowner";
    }

    if (collectionMatchesIdentity(list.contributors, identitySet)
        || collectionMatchesIdentity(list.listContributors, identitySet)
        || collectionMatchesIdentity(list.listcontributors, identitySet)) {
        return "contributor";
    }

    return null;
}

function upsertRuntimePermissionIndicator(listItem, permissionLabel) {
    if (!listItem || !permissionLabel) {
        return;
    }

    let indicator = listItem.querySelector(".runtime-list-permission");
    if (!indicator) {
        indicator = document.createElement("span");
        indicator.className = "runtime-list-permission";
        listItem.appendChild(document.createTextNode(" "));
        listItem.appendChild(indicator);
    }

    indicator.textContent = "[" + permissionLabel + "]";
    indicator.setAttribute("data-permission-level", permissionLabel);

    const allSpans = Array.from(listItem.querySelectorAll("span"));
    allSpans
        .filter((span) => span !== indicator)
        .forEach((span) => {
            if ((span.textContent || "").toLowerCase().includes("non-public")) {
                span.remove();
            }
        });
}

function annotateRuntimeAuthorizedPermissionIndicators(listsData, sessionData) {
    if (!Array.isArray(listsData) || listsData.length === 0) {
        return;
    }

    const runtimeRows = Array.from(document.querySelectorAll("li.runtime-authorized-list[data-render-source='runtime-authorized']"));
    if (runtimeRows.length === 0) {
        return;
    }

    const identitySet = getUserIdentitySet(sessionData);
    if (identitySet.size === 0) {
        return;
    }

    const permissionById = new Map();
    for (const list of listsData) {
        const listId = Number(list?.id);
        if (!Number.isFinite(listId)) {
            continue;
        }
        const label = getHighestListPermissionLabel(list, identitySet);
        if (label) {
            permissionById.set(listId, label);
        }
    }

    runtimeRows.forEach((row) => {
        const attrId = Number(row.getAttribute("data-list-id"));
        let listId = Number.isFinite(attrId) ? attrId : NaN;
        if (!Number.isFinite(listId)) {
            const href = row.querySelector("a")?.getAttribute("href") || "";
            const match = href.match(/\/lists\/(\d+)/);
            if (match) {
                listId = Number(match[1]);
            }
        }

        if (!Number.isFinite(listId)) {
            return;
        }

        const permissionLabel = permissionById.get(listId);
        if (permissionLabel) {
            upsertRuntimePermissionIndicator(row, permissionLabel);
        }
    });
}

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
    var lists = await getLists([id, username, role]);
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

        annotateRuntimeAuthorizedPermissionIndicators(lists, {
            id: id,
            userName: username,
            role: role
        });

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
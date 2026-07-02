
// Helper function to format tags for display - wraps tags with spaces in quotes
function formatTagsForDisplay(tags) {
    return tags.map(tag => {
        // If tag contains spaces, wrap it in quotes
        if (tag.includes(' ')) {
            return `"${tag}"`;
        }
        return tag;
    }).join(' ');
}

// Helper function to parse tags from input - handles quoted tags properly
function parseTagsFromInput(input) {
    const tags = [];
    const regex = /"([^"]+)"|(\S+)/g;
    let match;
    
    while ((match = regex.exec(input)) !== null) {
        // match[1] is for quoted strings, match[2] is for unquoted words
        tags.push(match[1] || match[2]);
    }
    
    return tags;
}

// Setup mode toggle functionality
function setupModeToggle(listid) {
    const newRadio = document.getElementById('new');
    const updateRadio = document.getElementById('update');
    
    // Add event listeners to both radio buttons
    newRadio.addEventListener('change', function() {
        if (this.checked) {
            // Switch to new mode - reload page with just listid
            const newUrl = `${window.location.pathname}?listid=${listid}`;
            window.location.href = newUrl;
        }
    });
    
    updateRadio.addEventListener('change', function() {
        if (this.checked) {
            // Keep current mode (update mode should already be loaded)
            // No action needed since we're already in update mode
            console.log('Staying in update mode');
        }
    });
}

document.addEventListener('DOMContentLoaded', getItem);

let currentListName = '';
let currentListVisibility = '';
let currentModerationItemId = null;
let currentModeratedItemId = null;
let currentItemId = null;
let currentItemListId = null;

function isListPublicVisibility(visibility) {
    if (visibility == null) return false;
    return String(visibility).toLowerCase() === 'public';
}

function updateFederationGuidance() {
    const visibleHelp = document.getElementById('federation-visible-help');
    const deletedHelp = document.getElementById('federation-deleted-help');
    const visibleCheckbox = document.getElementById('item.visible');
    const deletedCheckbox = document.getElementById('item.isdeleted');

    if (!visibleHelp || !deletedHelp || !visibleCheckbox || !deletedCheckbox) {
        return;
    }

    if (!isListPublicVisibility(currentListVisibility)) {
        visibleHelp.style.display = 'none';
        deletedHelp.style.display = 'none';
        return;
    }

    const listName = currentListName || '(unnamed list)';
    visibleHelp.textContent = visibleCheckbox.checked
        ? `Making this item Invisible will federate a Delete, because list ${listName} is Public.`
        : `Making this item Visible will federate this item again, because list ${listName} is Public.`;

    deletedHelp.textContent = deletedCheckbox.checked
        ? `Undeleting will federate this item again, because list ${listName} is Public.`
        : `Marking this item as deleted will federate a Delete, because list ${listName} is Public.`;

    visibleHelp.style.display = 'block';
    deletedHelp.style.display = 'block';
}

function setListHoverMetadata(listid, listName) {
    const listIdInput = document.getElementById('item.listid');
    if (!listIdInput) {
        return;
    }

    if (listName && listName.length > 0) {
        listIdInput.title = `List ${listid}: ${listName}`;
    } else {
        listIdInput.title = `List ${listid}`;
    }
}

function setupVisibilityFederationHandlers() {
    const visibleCheckbox = document.getElementById('item.visible');
    const deletedCheckbox = document.getElementById('item.isdeleted');

    if (visibleCheckbox) {
        visibleCheckbox.addEventListener('change', updateFederationGuidance);
    }

    if (deletedCheckbox) {
        deletedCheckbox.addEventListener('change', updateFederationGuidance);
    }
}

function updateModerationLinkBox() {
    const moderationBox = document.getElementById('moderation-link-box');
    if (!moderationBox) {
        return;
    }

    moderationBox.className = 'moderation-link-box';
    moderationBox.style.display = 'none';
    moderationBox.textContent = '';

    if (!Number.isFinite(currentItemId) || !Number.isFinite(currentItemListId)) {
        return;
    }

    if (currentModerationItemId != null) {
        const moderationUrl = `_edit.item.html?listid=${currentItemListId}&itemid=${currentModerationItemId}`;
        moderationBox.classList.add('moderated-item');
        moderationBox.innerHTML = `This item is under moderation. <a href="${moderationUrl}">Open moderation ticket #${currentModerationItemId}</a>.`;
        moderationBox.style.display = 'block';
        return;
    }

    if (currentModeratedItemId != null) {
        const moderatedUrl = `_edit.item.html?listid=${currentItemListId}&itemid=${currentModeratedItemId}`;
        moderationBox.classList.add('mod-ticket');
        moderationBox.innerHTML = `This is a moderation ticket. <a href="${moderatedUrl}">Open moderated item #${currentModeratedItemId}</a>.`;
        moderationBox.style.display = 'block';
    }
}



// function that is called when _edit.item.html is called that
// gets the listid from the url querystring and then 
// populates the form in _edit.item.html with the list data
async function getItem() {
    let fn = 'getItem'; console.log(fn)

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
        }
    
    let [id, username, role] = await amloggedin();
        console.debug(fn + ' -- username: ' + username);
        console.debug(fn + ' -- role: ' + role);
    

    // Get the itemid from the querystring
    let urlParams = new URLSearchParams(window.location.search);
    let itemid = urlParams.get('itemid');
    console.debug(fn + ' -- itemid: ' + itemid);
    let listid = urlParams.get('listid');
    console.debug(fn + ' -- listid: ' + listid);
    // set the value of the isSuggestion field with this value
    let suggestionBox = document.getElementById('isSuggestion');
    let isSuggestion = urlParams.get('suggestion');
    console.debug(fn + ' -- suggestion? ' + isSuggestion);
        
    if (!isSuperUser(role) && !isListOwner(role)){
        isSuggestion = true;   
    }
    suggestionBox.checked = isSuggestion;
    
    if (itemid == null || itemid == '') {
        // not necessarily bad; if we have a listid, then we are creating a new item

        if (listid == null || listid == '') {
            // ok now we panic
            d('No itemid or listid provided!');
            // disable the form submit action
            document.getElementById('edititemform').addEventListener('submit', function (e) {
                e.preventDefault();
            });
            // make the form fields readonly and greyed out

            document.getElementById('item.name').readOnly = true;
            document.getElementById('item.comment').readOnly = true;
            document.getElementById('item.tags').readOnly = true;
            document.getElementById('item.visible').disabled = true;
            document.getElementById('item.isdeleted').disabled = true;
            return;
        } else {


            // populate the listid field in the form
            document.getElementById('item.listid').value = listid;
            currentItemListId = Number(listid);
            if(isSuggestion) {
                // get the first <h1> tag in the DOM
                let h1 = document.querySelector('h1');
                
                h1.innerText = 'SUGGEST new item in list ' + listid;
                
                document.getElementById('item.visible').disabled = true;
                document.getElementById('item.visible').checked = false;
                document.getElementById('item.isdeleted').disabled = true;
                document.getElementById('item.isdeleted').checked = false;
                
                d('Creating SUGGESTION in list ' + listid + '.');
                c(RC.OK);
            } else {
                document.getElementById('item.visible').checked = true;
                document.getElementById('item.isdeleted').checked = false;
                d('Creating new item in list ' + listid + '.');
                c(RC.OK);
            }
        }


    }
    else {
        // we got a valid itemid, but we must also have a valid listid
        if (listid == null || listid == '') {
            // ok now we panic
            d('No listid provided!');
            // disable the form submit action
            document.getElementById('edititemform').addEventListener('submit', function (e) {
                e.preventDefault();
            });
            // make the form fields readonly and greyed out
            document.getElementById('item.name').readOnly = true;
            document.getElementById('item.comment').readOnly = true;
            document.getElementById('item.tags').readOnly = true;
            document.getElemebtById('item.visible').disabled = true;
            document.getElementById('item.isdeleted').disabled = true;
            return;
        } else {
            // populate the listid field in the form
            document.getElementById('item.listid').value = listid;
            // populate the itemid field in the form
            document.getElementById('item.id').value = itemid;
            currentItemId = Number(itemid);
            currentItemListId = Number(listid);
            d('Editing item ' + itemid + ' in list ' + listid + '.');
            c(RC.OK);
            // oh and set the radio button to "update"
            document.getElementById('update').checked = true;
            
        }
    }


    // assume this page is ON the server with the API
    // get the url of the API from the current page url
    apiUrl = window.location.href;
    // get just the hostname and port from the url
    apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.item.html'));
    console.debug(fn + ' -- apiUrl: ' + apiUrl);
    if (itemid != null) {
        console.debug(fn + ' -- Getting item ' + itemid + ' in list ' + listid + '.')
        fetch(apiUrl + '/items/' + itemid)
            .then(response => {
                if (response.ok) {
                    return response.json();
                }
                else  {
                    throw new Error(`${fn} - ${response.status}:${response.statusText}`);
                }
            })
            //.then(response => response.json())
            .then(data => {
                console.log('Success:', data);
                if (itemid != null && data.id != null && String(data.id) !== String(itemid)) {
                    const newUrl = `${window.location.pathname}?listid=${listid}&itemid=${data.id}`;
                    console.info(`${fn} -- item moved ${itemid} -> ${data.id}; reloading editor to canonical item URL: ${newUrl}`);
                    window.location.replace(newUrl);
                    return;
                }
                // Populate the form with the data from the API
                document.getElementById('item.id').value = data.id;
                document.getElementById('item.name').value = data.name;
                document.getElementById('item.visible').checked = data.visible;
                document.getElementById('item.isdeleted').checked = data.isDeleted === true;
                currentItemId = Number(data.id);
                currentItemListId = Number(data.listId);
                currentModerationItemId = data.moderationItemId != null ? Number(data.moderationItemId) : null;
                currentModeratedItemId = data.moderatedItemId != null ? Number(data.moderatedItemId) : null;
                //document.getElementById('item.comment').value = data.comment;
                easymde.value(data.comment);
                document.getElementById('item.tags').value = formatTagsForDisplay(data.tags);
                updateFederationGuidance();
                updateModerationLinkBox();
            })
            .catch((error) => {
                // write any error to the span with id="result"
                d(error);
                c(RC.ERROR);
                console.error(error);
            });
    }
    // Get the list NAME for the "back to list page" link
    fetch('/lists/' + listid)
        .then(handleResponse)
        .then(response => response.json())
        .then(data => {
            console.log('Success:', data);
            currentListName = data.name || '';
            currentListVisibility = data.visibility || '';
            // Populate the form with the data from the API
            document.getElementById('back2list').href = apiUrl + '/' + data.name + '.html';
            console.log(' | back2list.href: ' + document.getElementById('back2list').href);
            setListHoverMetadata(listid, currentListName);
            updateFederationGuidance();
        })
        .catch((error) => {
            // write any error to the span with id="result"
            d(error);
            c(RC.ERROR);
            document.getElementById('back2list').href = '/index.html';
            document.getElementById('back2list').innerText = 'Nonexistant List - Back to index';
            console.error('Error:', error);
        });

    // Add event listeners for radio button mode selection
    setupModeToggle(listid);
    setupVisibilityFederationHandlers();
    updateFederationGuidance();
    updateModerationLinkBox();

    // If the item ID was rotated on save, show a note after reload.
    if (urlParams.get('idrotated') === '1') {
        d('This item\'s canonical ID changed after visibility update; editor reloaded to the new ID.');
        c(RC.OK);
    }
}

// When the form is submitted, send it to the REST API
document.getElementById('edititemform').addEventListener('submit', updateItem);

async function updateItem(e) {
    let fn = 'updateItem';
    e.preventDefault();
    console.log('updateItem');
    let apiUrl = "";

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
        }
    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    let isSuggestion = document.getElementById("isSuggestion").checked;
    
    if (!isSuperUser(role) && !isListOwner(role) && !isSuggestion){
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }



    try {
        apiUrl = window.location.href;
        // get just the hostname and port from the url
        apiUrl = apiUrl.substring(0, apiUrl.indexOf('/_edit.item.html'));
        myUrl = apiUrl;
        console.debug(' | API URL: ' + apiUrl);

        let id = document.getElementById('item.id').value;
        let listid = document.getElementById('item.listid').value;
        let name = document.getElementById('item.name').value;
        let visible = document.getElementById('item.visible').checked; 
        let isDeleted = document.getElementById('item.isdeleted').checked;
        //let comment = document.getElementById('item.comment').value;
        let comment = easymde.value();
        //alert(comment);
        let tags = document.getElementById('item.tags').value;
        //split the tags 

        console.debug(' | id: ' + id);
        console.debug(' | listid: ' + listid);
        let data;
        let apiMethod;
        // if id is null or empty, then this is a new item
        // and we need to call the API to create a new item
        if (id == null || id == '') {
            
            if(isSuggestion) {
                apiUrl = '/lists/' + listid + '/suggest';
            } else {
                apiUrl = apiUrl + '/items';
            }
            data = { listid, name, comment, tags: parseTagsFromInput(tags), visible, isDeleted };
            apiMethod = 'POST';
        }
        else {
            if (listid == null || listid == '') {
                // ok now we panic
                d('No listid or item id provided!');
                return;
            } else {
                // if id is not null or empty, then this is an existing item
                // and we need to call the API to update the list
                apiUrl = apiUrl + '/items/' + id;
                data = { id, listid, name, comment, tags: parseTagsFromInput(tags), visible, isDeleted };
                apiMethod = 'PUT';
            }
        }
        
        let displayResults = "";
        let newID = null;
        // Call the REST API
        console.info(' | Calling API: ' + apiUrl + ' with data: ' + JSON.stringify(data));
        fetch(apiUrl, {
            method: apiMethod,
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(data),
        })
        .then(response => {
            if (response.ok) {
                return response;
            }
            else if (response.status == RC.NOT_FOUND) {
                throw new Error(`Item ${id} in list ${listid} not found!`);
            }
            else if (response.status == RC.UNAUTHORIZED) {
                throw new Error('Not authorized - have you logged in yet? <a href="_login.html">Login</a>');
            }
            else if (response.status == RC.FORBIDDEN) {
                throw new Error('Forbidden - have you logged in yet? <a href="_login.html">Login</a>');
            }
            else {
                throw new Error('Error ' + response.status + ' - ' + response.statusText);
            }
        })
            .then(response => {
                console.log('Response IS:', response);
                let contentType = response.headers.get("Content-Type");
                if (contentType == null) contentType = 'text/plain';
                console.log('Content-Type IS:', contentType);
                if (contentType.includes("application/json")) {
                    return response.json().then(json => {
                        newID = json.id;
                        console.log('NEW ID IS:', newID);
                        displayResults = "ITEM Created: " + JSON.stringify(json);
                        console.log('displayResults IS:', displayResults);
                        d(displayResults);

                        // now that we have newID, populdate the id field in the form
                        // but only if we're in "update" mode
                        // get the value of the radio button neworupdate
                        // if its new then leave the id field alone
                        let newOrUpdate = document.querySelector('input[name="neworupdate"]:checked').value;
                        console.debug(' | newOrUpdate: ' + newOrUpdate);
                        if (newOrUpdate == 'update') {
                            document.getElementById('item.id').value = newID;
                            const idChanged = json.itemIdChanged === true
                                || (id != null && id !== '' && String(newID) !== String(id));
                            if (idChanged) {
                                d(`Item ID changed from ${id} to ${newID}; reloading editor to the new canonical ID.`);
                                c(RC.OK);
                                const rotatedUrl = `${window.location.pathname}?listid=${listid}&itemid=${newID}&idrotated=1`;
                                window.location.replace(rotatedUrl);
                                return;
                            }

                            // and update the url in the browser to include the newID
                            const newUrl = `${window.location.pathname}?listid=${listid}&itemid=${newID}`;
                            window.history.pushState({}, '', newUrl);
                            console.debug(' | New URL: ' + newUrl);
                        }
                    });
                } else if (contentType.includes("text") || contentType == null) {
                    return response.text().then(text => {
                        displayResults = "Item modified! ";
                        console.log('displayResults IS:', displayResults);
                        d(displayResults);
                        c(RC.OK);
                    });
                } else {
                    return response.text().then(text => {
                        displayResults = "NO IDEA: " + text;
                        d(displayResults);
                        c(RC.ERROR);
                    });
                }


            });

    }
    catch (error) {
        d(error);
        c(RC.ERROR);
        console.error(error);
    }

}
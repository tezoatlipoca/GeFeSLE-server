let rightClickedLink;
let globalCanEditList = false;


function deleteItem(listId, itemid) {
    let fn = "deleteItem"; console.log(fn);
    if (islocal()) return;
    if (confirm('Are you sure you want to delete this item?')) {
        let apiUrl = 'deleteitem/' + listId + '/' + itemid;
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
                    localStorage.setItem('result', 'Item ' + itemid + ' in list ' + listId + ' deleted successfully');

                    // just refresh the page
                    location.reload();
                }
                else if (response.status == RC.UNAUTHORIZED) {
                    let msg = "Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>";
                    throw new Error(msg);
                }
                else if (response.status == RC.FORBIDDEN) {
                    let msg = "Forbidden to delete items! Are you logged in? <a href=\"_login.html\">LOGIN</a><br>You also need to be SuperUser or listowner.";
                    throw new Error(msg);
                }
                else {
                    d('Error: ' + response.statusText);
                    c(RC.ERROR);
                }

            })
            .catch((error) => {
                console.error(fn + error);
                d(error);
                c(RC.ERROR);
            });
    }
}





function filterUpdate() {
    console.info('filterUpdate');

    // get the rows of the table
    let rows = document.getElementsByClassName('itemrow');
    // get the total number of rows; save for later
    let totesrows = rows.length;

    // get the value of the textsearchbox field
    let textsearch = document.getElementById('textsearchbox').value;
    // convert it to array of texttags called texttags
    const texttags = textsearch.split(' ');
    // annoyingly if the user enters a space at the end of the search string,
    // it will create an empty tag at the end of the array
    // so if the last element of the array is empty, remove it
    if (texttags[texttags.length - 1] == '') {
        texttags.pop();
    }

    // get the value of the tagssearchbox field
    let tagssearch = document.getElementById('tagsearchbox').value;
    // convert it to an array of tags called tagstags
    const tagstags = tagssearch.split(' ');
    // annoyingly if the user enters a space at the end of the search string, 
    // it will create an empty tag at the end of the array
    // so if the last element of the array is empty, remove it
    if (tagstags[tagstags.length - 1] == '') {
        tagstags.pop();
    }

    let numVisibleTags = 0;
    //rows.length
    if (totesrows > 0) {
        for (let i = 0; i < rows.length; i++) {


            console.debug('rows[i]:', rows[i]);
            let nameCell = rows[i].previousElementSibling;
            let commentCell = rows[i].getElementsByClassName('commentcell')[0];
            let tagsCell = rows[i].getElementsByClassName('tagscell')[0];


            let itemnametext = nameCell ? nameCell.innerText : '';
            let rowcommtext = commentCell ? commentCell.innerText : '';
            let rowtext = itemnametext + ' ' + rowcommtext;
            let foundtext = false;


            // if there are no texttags then foundtext is true (searching for nothing returns everything)
            if (texttags.length == 0) {
                foundtext = true;
                console.debug('rowtext:', rowtext, 'texttags:', texttags, 'foundtext: TRUE because no search text');
            }
            else {
                foundtext = texttags.some(tag => rowtext.includes(tag));
                console.debug('rowtext:', rowtext, 'texttags:', texttags, 'foundtext:', foundtext);
            }

            let rowtagstext = tagsCell ? tagsCell.innerText : '';

            let foundtags = false;
            // if there are no texttags then foundtages is true (searching for nothing returns everything)
            if (tagstags.length == 0) {
                foundtags = true;
                console.debug('rowtagstext:', rowtagstext, 'tagstags:', tagstags, 'foundtags: TRUE because no search tags');
            }
            else {
                foundtags = tagstags.some(tag => rowtagstext.includes(tag));
                console.debug('rowtagstext:', rowtagstext, 'tagstags:', tagstags, 'foundtags:', foundtags);
            }

            if (foundtags && foundtext) {
                rows[i].style.display = '';
                nameCell.style.display = '';
                numVisibleTags++;
                console.debug('visible row!');
            }
            else {
                rows[i].style.display = 'none';
                nameCell.style.display = 'none';
                console.debug('hidden row!');
            }

            // change the result span to show the number of visible rows
            d('DISPLAYING ' + numVisibleTags + ' of ' + totesrows + ' items in this list.');
            c(RC.OK);
        }
    }
    else {
        d('NO ITEMS in this list');
        c(RC.OK);
    }
}



// function to retreive a list of listids and listnames from the REST API
async function loadLists() {
    let fn = '/lists'; console.debug(fn);
    let apiUrl = '/lists';
    let tuples = [];

    try {
        let response = await fetch(apiUrl);
        if (!response.ok) {
            throw new Error(`${fn}: ${response.status}:${response.statusText}`);
        }
        let data = await response.json();
        console.debug(`${fn} ${apiUrl} -> ${JSON.stringify(data)}`);
        for (let list of data) {
            tuples.push([list.id, list.name]);
        }
        return tuples;
    } catch (error) {
        d(error);
        c(RC.ERROR);
        console.error(fn + error);
    }
}






async function createQuickMoveMenu() {
    let fn = 'createQuickMoveMenu'; console.debug(fn);
    // get all of the lists
    let lists = await loadLists();

    // add some html to the page
    let menuHtml = '<div id="contextMenu" class="context-menu">';
    // list name is in the first h1 class="listtitle" element
    // but we strip off the link back to the index.
    let listname = document.querySelector('.listtitle').innerText.replace(document.querySelector('.listtitle .indexlink').innerText, '');
    // and the leading space
    listname = listname.substring(1);

    for (let list of lists) {
        // if the list is the current list, don't show it in the menu
        if (listname == list[1]) {
            continue;
        }
        else {
            menuHtml += `<a href="#" id="list${list[0]}" class="context-menu-link-regular">${list[1]}</a>`;
        }
    }

    menuHtml += '</div>';

    console.debug(fn + ' | menuHtml: ' + menuHtml);
    document.body.insertAdjacentHTML('beforeend', menuHtml);

    for (let list of lists) {
        if (listname != list[1]) {
            let link = document.getElementById(`list${list[0]}`);
            link.addEventListener('click', function (event) {
                console.debug(`LINK ${currentItemRowId} listid: ${list[0]} listname: ${list[1]}`);
                moveItem(currentItemRowId, list[0]);
            });
        }
    }
}





function showContextMenu(e) {
    let fn = 'showContextMenu'; console.debug(fn);
    e.preventDefault();
    rightClickedLink = e.target;

    let itemRow = rightClickedLink.closest('.itemrow');
    if (itemRow) {
        currentItemRowId = itemRow.id;
    }


    var contextMenu = document.getElementById('contextMenu');
    contextMenu.style.display = 'block';
    contextMenu.style.left = (e.pageX - contextMenu.offsetWidth) + 'px';
    contextMenu.style.top = e.pageY + 'px';

    return false; // prevents the browser's context menu from appearing
};

// Hide the context menu when the user clicks elsewhere
window.addEventListener('click', function (e) {
    document.getElementById('contextMenu').style.display = 'none';
});


async function moveItem(itemid, listid) {
    let fn = 'moveItem'; console.debug(fn);
    let apiUrl = "/moveitem";

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }
    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)) {
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }



    try {
        console.debug(' | API URL: ' + apiUrl);

        let data;
        let apiMethod;
        // if id is null or empty, then this is a new item
        // and we need to call the API to create a new item
        // make sure both itemid and listid are int
        itemid = parseInt(itemid);
        listid = parseInt(listid);

        data = { itemid, listid };
        apiMethod = 'POST';

        let formPOST = JSON.stringify(data);
        console.info(`${fn} <- ${formPOST}`);
        fetch(apiUrl, {
            method: apiMethod,
            headers: {
                'Content-Type': 'application/json',
            },
            body: formPOST,
        })
            .then(handleResponse)
            .then(response => {
                if (response.ok) {
                    return response.text();
                }
                else if (response.status == RC.NOT_FOUND) {
                    // the actual reason why is in the response body, so we need to read it
                    return response.text().then(text => {
                        throw new Error('MOVEITEM: ' + text);
                    });
                }
            })
            .then(text => {
                d(text);
                c(RC.OK);
                // asyncronously wait 1 second before reloading the page
                //setTimeout(function () {
                    location.reload();
                //}, 1000);
            })
            .catch((error) => {
                d(error);
                c(RC.ERROR);

            });
    }
    catch (error) {
        d(error);
        c(RC.ERROR);
        console.error('Error:', error);
    }

}


function buildTagsMenu() {
    fn = 'buildTagsMenu'; console.debug(fn);
    if (!globalCanEditList) {
        console.debug(fn + ' | Not authorized to edit list - suppress tags menu');
        return;
    }
    document.querySelectorAll('.tagscell').forEach(cell => {
        cell.addEventListener('click', function (e) {
            e.preventDefault();

            // Check if the right-clicked element is the cell itself
            if (e.target === this) {
                // Show the context menu

                let tag = prompt("Add tags (seperated by space):");
                if (tag) {
                    //alert("You entered: " + tag);
                    // add the tag to the cell
                    let span = document.createElement('span');
                    span.innerText = tag;
                    span.className = 'tag';
                    this.appendChild(span);
                    addTag(this.closest('.itemrow').id, tag);
                }

            }
            else {
                // Otherwise, the right-clicked element is a child of the cell
                // get the value of the span
                let tag = e.target.innerText;
                //alert("You clicked on the tag: " + tag);
                // delete the span entirely
                e.target.remove();
                removeTag(this.closest('.itemrow').id, tag);

            }
        });
    });



}



async function addTag(itemid, tag) {
    let fn = 'addTag'; console.debug(fn);
    let apiUrl = "/addtag";

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }
    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)) {
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }



    try {
        console.debug(' | API URL: ' + apiUrl);

        let data;
        let apiMethod;
        // if id is null or empty, then this is a new item
        // and we need to call the API to create a new item
        // make sure both itemid and listid are int
        itemid = parseInt(itemid);


        data = { itemid, tag };
        apiMethod = 'POST';

        let formPOST = JSON.stringify(data);
        console.info(`${fn} <- ${formPOST}`);
        fetch(apiUrl, {
            method: apiMethod,
            headers: {
                'Content-Type': 'application/json',
            },
            body: formPOST,
        })
            .then(handleResponse)
            .then(response => {
                if (response.ok) {
                    return response.text();
                }
                else if (response.status == RC.NOT_FOUND) {
                    // the actual reason why is in the response body, so we need to read it
                    return response.text().then(text => {
                        throw new Error('ADDTAG: ' + text);
                    });
                }

            })
            .then(text => {
                d(text);
                c(RC.OK);
                // asyncronously wait 1 second before reloading the page
                // setTimeout(function () {
                //     location.reload();
                // }, 1000);
            })
            .catch((error) => {
                d(error);
                c(RC.ERROR);

            });
    }
    catch (error) {
        d(error);
        c(RC.ERROR);
        console.error('try/catch Error:',);
    }

}

async function removeTag(itemid, tag) {
    let fn = 'removeTag'; console.debug(fn);
    let apiUrl = "/removetag";

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }
    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)) {
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }



    try {
        console.debug(' | API URL: ' + apiUrl);

        let data;
        let apiMethod;
        // if id is null or empty, then this is a new item
        // and we need to call the API to create a new item
        // make sure both itemid and listid are int
        itemid = parseInt(itemid);


        data = { itemid, tag };
        apiMethod = 'POST';

        let formPOST = JSON.stringify(data);
        console.info(`${fn} <- ${formPOST}`);
        fetch(apiUrl, {
            method: apiMethod,
            headers: {
                'Content-Type': 'application/json',
            },
            body: formPOST,
        })
            .then(handleResponse)
            .then(response => {
                if (response.ok) {
                    return response.text();
                }
                else if (response.status == RC.NOT_FOUND) {
                    // the actual reason why is in the response body, so we need to read it
                    return response.text().then(text => {
                        throw new Error('REMOVETAG: ' + text);
                    });
                }

            })
            .then(text => {
                d(text);
                c(RC.OK);
                // asyncronously wait 1 second before reloading the page
                // setTimeout(function () {
                //     location.reload();
                // }, 1000);
            })
            .catch((error) => {
                d(error);
                c(RC.ERROR);

            });
    }
    catch (error) {
        d(error);
        c(RC.ERROR);
        console.error('Error:', error);
    }

}


// on page load, call the filterTAGSUpdate function
window.onload = async function () {
    let fn = 'list_view.js - window.onload';
    if (localStorage.getItem('result')) {
        document.getElementById('result').innerHTML = localStorage.getItem('result');
        localStorage.removeItem('result');
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

    if (isSuperUser(role) || isListOwner(role)) {
        console.debug(fn + ' | logged in and either isSuperUser or isListOwner');
        showListSecrets();
        showDebuggingElements();
        showAdminSecrets();
        globalCanEditList = true;

    }


    // call the filterUpdate function (just to show how manyitems there are)
    filterUpdate();
    // turn any 1st cell links into links
    make1stcelllinks();
    // create the quick move menus
    createQuickMoveMenu();
    // create the quick tag add/remove 
    buildTagsMenu();
}


async function importItems(sourceService, destLIst) {
    // cancel the default action
    event.preventDefault();
    let fn = 'importItems'; console.debug(fn);

    if (islocal()) {
        d("Cannot call API from a local file!");
        c(RC.BAD_REQUEST);
        return;
    }
    let [id, username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);
    if (!isSuperUser(role) && !isListOwner(role)) {
        d("You are not logged in! <a href='_login.html'>Login here.</a>");
        c(RC.UNAUTHORIZED);
        return;
    }

    // GeListImportDto is in the form of:
    // { Service:"Service:subservice", Data:"data"}
    // valid Services are (see ImportService class)
    // Microsoft:StickyNotes
    // Google:Tasks
    // Mastodon:Bookmarks
    // server side will worry about validating this
    // BUT WAIT! some services need addition info. 
    // e.g. if the service is Mastodon we need to know how many bookmarks to get and whether to unbookmark them
    let importService = null;
    if (sourceService == 'Mastodon:Bookmarks') {
        mastoBookmarkLoadDefaults();
        await showModalAndGetValues().then(values => {
            if (values) {
                alert(`You entered: ${JSON.stringify(values)}`);
                importService = { Service: sourceService, Data: JSON.stringify(values) };
                mastoBookmarkSaveDefaults();
            } else {
                console.log("Modal was dismissed");
                return;
            }
        });
    }
    else if (sourceService == 'Google:Tasks') {
        // this populates the list of task lists the user has
        await populateGoogleTaskLists(sourceService);
        // this shows the modal
        await showModalGoogleTaskLists().then(value => {
            if (value) {
                alert(`You chose list: ${value}`);
                importService = { Service: sourceService, Data: value };
            }
            else {
                console.log("Modal was dismissed");
                return;
            }
        })
    }
    else {
        importService = { Service: sourceService, Data: '' };

    }
    if (!importService) {
        return;
    }

    let processtoken = null;
    let apiUrl = '/lists/' + destLIst;
    let apiMethod = 'POST';
    console.info(`${fn} <- ${JSON.stringify(importService)}`);
    await fetch(apiUrl, {
        method: apiMethod,
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(importService)
    })
        .then(handleResponse)
        // if success it will return a process token which can be used to check status
        .then(response => response.text())
        .then(data => {
            processtoken = data;
            console.debug('Process Token:', processtoken);
            d(processtoken);
            c(RC.OK);
        })
        .catch((error) => {
            d(error);
            c(RC.ERROR);

        });
    // now, every half a second check the progress endpoint until the response is "Completed"
    console.debug('Process Token:', processtoken);
    let status = null;
    while (status !== "Completed") {
        status = await checkImportStatus(processtoken);
        if (!status) {
            break;
        }
        else {
            let statusObj = JSON.parse(status);
            if (statusObj === "Completed") {
                break;
            }

        }
        console.debug('Status:', status);
        d(status);
        c(RC.OK);
        // wait 1 second before checking again
        await new Promise(r => setTimeout(r, 500));
    }
    d(`${status} - Import completed! Refreshing page in 3 seconds..`);
    c(RC.OK);
    // asyncronously wait 1 second before reloading the page
    setTimeout(function () {
        location.reload();
    }, 3000);
}

document.querySelectorAll('.commentcell').forEach(cell => {
    cell.addEventListener('click', () => {
        cell.classList.toggle('expanded');
    });
});

window.addEventListener('load', function () {
    const commentCells = document.querySelectorAll('.commentcell');
    commentCells.forEach(cell => {
        if (cell.scrollHeight > cell.clientHeight) {
            cell.classList.add('overflow');
        }
    });
});


async function checkImportStatus(processToken) {
    let fn = 'checkImportStatus'; console.debug(fn);
    let apiMethod = 'GET';
    console.debug('processToken:', processToken);
    // if processToken has any quotes around it, remove them
    processToken = processToken.replace(/['"]+/g, '');
    let apiUrl = '/checkprogress/' + processToken;

    console.info(`${fn} <- ${processToken}`);
    return fetch(apiUrl, {
        method: apiMethod,
        headers: {
            'Content-Type': 'application/json',
        }
    })
        .then(handleResponse)
        .then(response => {
            console.debug('Response:', response);
            return response.text();
        })
        .catch((error) => {
            d(error);
            c(RC.ERROR);

        });
}


async function reportItem(listId, itemid) {
    let fn = "reportItem"; console.log(fn);
    if (islocal()) return;
    let areUsure = false;
    let reportreason = await showModalReportForm();
    if (reportreason == null) {
        return;
    }
    else {
        areUsure = confirm('Are you sure you want to report this item? (for reason: ' + reportreason + ')')
    }


    if (areUsure) {
        let apiUrl = 'items/' + itemid + '/report';
        let data = new URLSearchParams();
        data.append('reason', reportreason);
        console.debug(`${fn} --> ${apiUrl} <== {reason: ${reportreason}} `);
        fetch(apiUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                "GeFeSLE-XMLHttpRequest": "true"
            },
            body: data  // refactor all post endpoints to handle crap body an absense of content-types
        })
            .then(handleResponse)
            .then(response => {
                console.log('Response IS:', response);
                // if the response is ok, redirect to the list page
                if (response.ok) {
                    // save ourselves a result message in localstorage
                    localStorage.setItem('result', 'Item ' + itemid + ' in list ' + listId + ' deleted successfully');

                    // just refresh the page
                    location.reload();
                }

            })
            .catch((error) => {
                console.error(error);
                d(error);
                c(RC.ERROR);
            });
    }
}
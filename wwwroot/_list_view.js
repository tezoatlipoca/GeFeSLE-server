let rightClickedLink;
let globalCanEditList = false;


function deleteItem(listId, itemid) {
    let fn="deleteItem / ";

    console.log(fn);
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

    // get the table
    let table = document.getElementById('itemtable');
    // get the rows of the table
    let rows = table.getElementsByTagName('tr');
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
            let rowcols = rows[i].getElementsByTagName('td');
            let itemnametext = rowcols[0].innerText;
            let rowcommtext = rowcols[1].innerText;
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

            let rowtagstext = rowcols[2].innerText;

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
                numVisibleTags++;
                console.debug('visible row!');
            }
            else {
                rows[i].style.display = 'none';
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
    let fn = 'loadLists'; console.debug(fn);    
    let apiUrl = '/showlists';
    let tuples = [];

    try {
        let response = await fetch(apiUrl);
        if (!response.ok) {
            d('No lists found at this URL: ' + apiUrl);
            console.error(' | Error calling API: ' + response.status + ' ' + response.statusText);
            return;
        }
        let data = await response.json();
        console.debug(`${fn} ${apiUrl} -> ${JSON.stringify(data)}`);
        for (let list of data) {
            tuples.push([list.id, list.name]);
        }
        return tuples;
    } catch (error) {
        d('EXCEPTION loading lists - no lists found at this URL:' + apiUrl);
        console.error(' | EXCEPTION calling API: ' + error);
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
        if(list[1] == listname) {
            continue;
        }
        
        document.getElementById(`list${list[0]}`).addEventListener('click', function() {
            // call your function here and pass the parameters dynamically
            let itemId = rightClickedLink.closest('tr').id;
            console.debug(`LINK ${itemId} listid: ${list[0]} listname: ${list[1]}`);
            moveItem(itemId, list[0]);
        });
    }

}





function showContextMenu(e) {
    e.preventDefault();
    rightClickedLink = e.target;

    var contextMenu = document.getElementById('contextMenu');
    contextMenu.style.display = 'block';
    contextMenu.style.left = (e.pageX - contextMenu.offsetWidth) + 'px';
    contextMenu.style.top = e.pageY + 'px';

    return false; // prevents the browser's context menu from appearing
};

// Hide the context menu when the user clicks elsewhere
window.addEventListener('click', function(e) {
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
    if (!isSuperUser(role) && !isListOwner(role)){
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
        .then(text => {
            d(text);
            c(RC.OK);
            // asyncronously wait 1 second before reloading the page
            setTimeout(function () {
                location.reload();
            }, 1000);
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
        cell.addEventListener('click', function(e) {
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
                    addTag(this.closest('tr').id, tag);
                }

            }
            else {
                // Otherwise, the right-clicked element is a child of the cell
                // get the value of the span
                let tag = e.target.innerText;
                //alert("You clicked on the tag: " + tag);
                // delete the span entirely
                e.target.remove();
                removeTag(this.closest('tr').id, tag);

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
    if (!isSuperUser(role) && !isListOwner(role)){
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
        console.error('try/catch Error:', );
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
    if (!isSuperUser(role) && !isListOwner(role)){
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

    if(username != null) {
        console.debug(fn + ' | logged in');
        links = document.getElementsByClassName('pwdchangelink');
        for (let l of links) { l.style.display = ''; }
        links = document.getElementsByClassName('loginlink');
        for (let l of links) { l.style.display = 'none'; }
        }

    if(isSuperUser(role) || isListOwner(role) ) {
        console.debug(fn + ' | logged in and either isSuperUser or isListOwner');
        showListSecrets();
        showDebuggingElements();
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
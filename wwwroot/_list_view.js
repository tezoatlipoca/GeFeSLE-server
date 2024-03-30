
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
            document.getElementById('result').innerHTML = 'DISPLAYING ' + numVisibleTags + ' of ' + totesrows + ' items in this list.';
        }
    }
    else {
        document.getElementById('result').innerHTML = 'NO ITEMS in this list';
    }
}

// on page load, call the filterTAGSUpdate function
window.onload = async function () {
    let fn = 'list_view.js - window.onload';
    if (localStorage.getItem('result')) {
        document.getElementById('result').innerHTML = localStorage.getItem('result');
        localStorage.removeItem('result');
    }

    let [username, role] = await amloggedin();
    console.debug(fn + ' | username: ' + username);
    console.debug(fn + ' | role: ' + role);

    if(isSuperUser(role) || isListOwner(role) ) {
        console.debug(fn + ' | logged in and either isSuperUser or isListOwner');
        showListSecrets();
        showDebuggingElements();
    }


    //
    filterUpdate();
    // lastly call the function that checks first cell for links and makes them clickable
    make1stcelllinks();
}


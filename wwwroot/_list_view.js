


// when the page loads see if there's a stored value in localStorage
// if there is write it to the results span, then clear it from localStorage
// this is how we can pass msgs between static pages
window.onload = function () {
    if (localStorage.getItem('result')) {
        document.getElementById('result').innerHTML = localStorage.getItem('result');
        localStorage.removeItem('result');
    }
}



function deleteItem(listId, itemid) {
    console.log('deleteItem');
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
                    console.debug(' | DELETEITEM - Not authorized to set roles for user ' + username);
                    throw new Error('Not authorized! Have you logged in yet? <a href=\"_login.html\">LOGIN</a>');
                }
                else if (response.status == RC.FORBIDDEN) {
                    console.debug(' | DELETEITEM - Forbidden to get user ' + username);
                    throw new Error('Forbidden! Are you logged in as an admin?');
                }
                else {
                    d('Error: ' + response.statusText);
                    c(RC.ERROR);
                }

            })
            .catch((error) => {
                console.error('Error:', error);
                d(error);
                c(RC.ERROR);
            });
    }
}

function exportList(listId) {
    console.log('exportList');
    if (islocal()) {
        util.d('Can\'t export; viewing local html file');
        return;
    }
    fetch('export/' + listId, {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
            "GeFeSLE-XMLHttpRequest": "true"
        },
    })
        .then(response => {
            console.log('Response IS:', response);
            // if the response is ok, the result is the name of the json file in wwwroot
            if (response.ok) {
                // change the target href of the exportlink a tag to the file name
                response.text().then((text) => {
                    document.getElementById('exportlink').href = listId + '.json';
                    document.getElementById('exportlink').text = listId + '.json';
                });

            }
            else {
                util.d('Error: ' + response.statusText);
            }

        })
        .catch((error) => {
            console.error('Error:', error);
            d(error);
            c(RC.ERROR);
        });
}



function filterTAGSUpdate() {
    console.info('filterUpdate');

    // get the table
    let table = document.getElementById('itemtable');
    // get the rows of the table
    let rows = table.getElementsByTagName('tr');
    // get the total number of rows; save for later
    let totesrows = rows.length;
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
            // get the contents of 1st and second cell in the row
            console.debug('rows[i]:', rows[i]);
            let rowtagstext = rows[i].getElementsByTagName('td')[2].innerText;
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
            if (foundtags) {
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
    console.info('window.onload');
    let role = await getRole();
    console.debug('role:', role);
    // if the user isn't a list owner, hide the edit list link
    if(isSuperUser(role) || isListOwner(role)) {
        // show the stuff
    } else {
        let editlink = document.getElementsByClassName('editlink');
        // set the display style of the editlink to none
        editlink[0].style.display = 'none';

    }
    // if the user isn't a contributor (or higher), hide the add and edit item links
    if(isSuperUser(role) || isListOwner(role) || isContributor(role)) {
        // show stuff
    } else {
        let itemlink = document.getElementsByClassName('edititemlink');
        itemlink[0].style.display = 'none';
        // hide any a's on the page with class=itemeditlink or class=itemdeletelink
        let spans = document.getElementsByTagName('span');
        for (let s of spans) {
            if (s.className == 'itemeditlink' || s.className == 'itemdeletelink') {
                s.style.display = 'none';
            }
        }
    }
    //
    filterTAGSUpdate();
    // lastly call the function that checks first cell for links and makes them clickable
    make1stcelllinks();
}


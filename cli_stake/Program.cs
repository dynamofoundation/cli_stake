using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;

namespace cli_stake
{
    internal class Program
    {
        static void Main(string[] args_in)
        {
            Console.WriteLine("Dynamo coin staking utiity");
            Console.WriteLine("VERSION 1.0");
            Console.WriteLine("");
            Console.WriteLine("This utility allows users to interact with the DMO proof of stake system.  It will connect");
            Console.WriteLine("to any fullnode running an RPC server and hosting a wallet.  Command supported are:");
            Console.WriteLine("  stake - stake coins");
            Console.WriteLine("  unstake - unstake coins");
            Console.WriteLine("  claim - claim a staking ticket for a block");
            Console.WriteLine("  payout - request staking earnings payout");
            Console.WriteLine("");
            Console.WriteLine("Important - all coins to be staked must be in a single address.  This utility does not");
            Console.WriteLine("scan HD wallets for derived addresses.  Note that claim and payout transactions require");
            Console.WriteLine("fees, so please consider the cost of staking in the amount you want to stake.");
            Console.WriteLine("");
            Console.WriteLine("USAGE:  cli_stake");
            Console.WriteLine("    rpcconnect=<server name or ip>");
            Console.WriteLine("    rpcport=<port number for rpc>");
            Console.WriteLine("    rpcuser=<rpc username>");
            Console.WriteLine("    rpcpassword=<rpc password>");
            Console.WriteLine("    wallet=<name of wallet>");
            Console.WriteLine("    walletpass=<wallet password>  (optional)");
            Console.WriteLine("    command=[stake|unstake|claim|payout]");
            Console.WriteLine("    address=<address to stake> (also used as payment address for ticket, claim and unstake)");
            Console.WriteLine("    amount=<DMO to stake> (only used for stake command)");
            Console.WriteLine("    stake_type=[flex|lock]  (only used for stake command)");
            Console.WriteLine("    stake_term=[30|60|90|120|180|360|720]  (only used for stake command)");
            Console.WriteLine("    txid=<txid of stake to ticket, claim or unstake>  (not used for stake command)");
            Console.WriteLine("");
            Console.WriteLine("");

            Dictionary<string, string> args = new Dictionary<string, string>();
            foreach (string s in args_in)
            {
                string[] arg = s.Split("=");
                args.Add(arg[0], arg[1]);
            }

            //TODO - check for optional/required params and error out

            string command;
            string result;

            List<string> utxo = new List<string>();

            Utility.URI_base = "http://" + args["rpcconnect"] + ":" + args["rpcport"];
            Utility.username = args["rpcuser"];
            Utility.password = args["rpcpassword"];


            //open the wallet - will error if already open, no big deal
            if (args["wallet"].Length > 0)
            {
                command = "{ \"id\": 0, \"method\" : \"loadwallet\", \"params\" : [ \"" + args["wallet"] + "\" ] }";
                result = Utility.rpcExec(command);

                if (args.ContainsKey("walletpass"))
                    if (args["walletpass"].Length > 0)
                    {
                        command = "{ \"id\": 0, \"method\" : \"walletpassphrase\", \"params\" : [ \"" + args["walletpass"] + "\", 600 ] }";
                        result = Utility.rpcExec(command, args["wallet"]);

                    }
            }


            //find all unspent outputs and sum up amounts, store TXIDs so we can form the transaction and calculate change
            command = "{ \"id\": 0, \"method\" : \"listunspent\", \"params\" : [ 0, 999999999, [\"" + args["address"] + "\"] ] }";
            result = Utility.rpcExec(command, args["wallet"]);

            dynamic dResult = JObject.Parse(result);

            decimal total = 0;
            decimal dAmountToStake = Convert.ToDecimal(args["amount"]);

            foreach (dynamic o in dResult.result)
            {
                string txid = o.txid;
                UInt32 vout = o.vout;
                string address = o.address;
                decimal amount = o.amount;

                utxo.Add(txid + "," + vout + "," + amount.ToString(new CultureInfo("en-US")));

                total += amount;
                if (total >= dAmountToStake)
                    break;
            }

            //todo - check total >= amount to be staked

            string message = "";

            //create message to mine into op_return output
            if (args["command"] == "stake")
                message = "st01" + MakeStakeCommand(args["address"], args["stake_type"], args["stake_term"]);

            else if (args["command"] == "unstake")
                message = "st01" + MakeTXIDCommand("unstake", args["txid"]);

            else if (args["command"] == "claim")
                message = "st01" + MakeTXIDCommand("claim", args["txid"]);

            else if (args["command"] == "payout")
                message = "st01" + MakeTXIDCommand("payout", args["txid"]);


            command = "{ \"id\": 0, \"method\" : \"dumpprivkey\", \"params\" : [ \"" + args["address"] + "\" ] }";
                dynamic privkey = JObject.Parse(Utility.rpcExec(command, args["wallet"]));
                string strPrivkey = privkey.result;


            //amount to burn, less gas fee
            decimal change = total - dAmountToStake - 0.0001m;

            //assemble list of unspent outputs to include in transaction
            string input = "";
            foreach (string iUTXO in utxo)
            {
                string[] strUtxo = iUTXO.Split(',');
                input += "{\"txid\":\"" + strUtxo[0] + "\",\"vout\":" + strUtxo[1] + "},";
            }

            input = input.Substring(0,input.Length - 1);

            string output1 = "{\"" + args["address"] + "\":" + change.ToString(new CultureInfo("en-US")) + "}";
            string output2 = "{\"op_return_amount\": " + args["amount"] + "}";
            string output3 = "{\"data\":\"" + Utility.ByteArrayToHexString(Encoding.ASCII.GetBytes(message)) + "\"}";

            string txparams = "[ [" + input + "], [" + output1 + "," + output2 + "," + output3 + "]]";

            //create the transaction
            command = "{ \"id\": 0, \"method\" : \"createrawtransaction\", \"params\" : " + txparams + " }";
            dynamic dTransaction = JObject.Parse(Utility.rpcExec(command));

            //sign the transaction
            command = "{ \"id\": 0, \"method\" : \"signrawtransactionwithkey\", \"params\" : [\"" + dTransaction.result + "\", [\"" + strPrivkey + "\"] ]}";
            dynamic dSignedTransaction = JObject.Parse(Utility.rpcExec(command));

            //submit transaction to mempool for inclusion in blockchain
            command = "{ \"id\": 0, \"method\" : \"sendrawtransaction\", \"params\" : [\"" + dSignedTransaction.result.hex + "\"] }";
            dynamic dTXID = JObject.Parse(Utility.rpcExec(command));

            Console.WriteLine("Transaction ID: " + dTXID.result);
                
        }

        class StakeCommandLock
        {
            public string command;
            public string wallet;
            public string type;
            public int term;
        }

        class StakeCommandFlex
        {
            public string command;
            public string wallet;
            public string type;
        }

        class TXIDCommand
        {
            public string command;
            public string txid;
        }

        static string MakeStakeCommand ( string wallet, string type, string term )
        {
            if (type == "flex")
            {
                StakeCommandFlex s = new StakeCommandFlex();
                s.command = "stake";
                s.wallet = wallet;
                s.type = type;
                return JsonConvert.SerializeObject(s);
            }
            else
            {
                StakeCommandLock s = new StakeCommandLock();
                s.command = "stake";
                s.wallet = wallet;
                s.type = type;
                s.term = Convert.ToInt32(term);
                return JsonConvert.SerializeObject(s);
            }
        }


        static string MakeTXIDCommand ( string command, string txid )
        {
            TXIDCommand cmd = new TXIDCommand();
            cmd.command = command;
            cmd.txid = txid;
            return JsonConvert.SerializeObject(cmd);
        }



    }
}